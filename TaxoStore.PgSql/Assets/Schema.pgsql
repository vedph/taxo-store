-- ============================================
-- EXTENSION: pg_trgm
-- Required for GIN trigram indexes used by all ILIKE '%pattern%' searches.
-- ============================================

CREATE EXTENSION IF NOT EXISTS pg_trgm;

-- ============================================
-- TABLE: tree
-- ============================================

CREATE TABLE tree (
    id          VARCHAR(100) PRIMARY KEY,
    name        VARCHAR(500) NOT NULL,
    note        VARCHAR(5000)
);

-- B-tree for ORDER BY name in GetTreesAsync
CREATE INDEX idx_tree_name ON tree(name);

-- Trigram for GetTreesAsync: name ILIKE '%pattern%'
CREATE INDEX idx_tree_name_trgm ON tree USING gin(name gin_trgm_ops);

-- ============================================
-- TABLE: node
-- ============================================

CREATE TABLE node (
    id          SERIAL PRIMARY KEY,
    parent_id   INT REFERENCES node(id) ON DELETE CASCADE,
    tree_id     VARCHAR(100) NOT NULL REFERENCES tree(id) ON DELETE CASCADE,
    key         VARCHAR(500) NOT NULL,
    label       VARCHAR(1000) NOT NULL,
    label_ix    VARCHAR(1000) NOT NULL,
    flags       VARCHAR(50) NOT NULL DEFAULT '',
    note        VARCHAR(5000),

    -- Unique key within each tree; also serves as a B-tree index on (tree_id, key)
    -- for GetNodeFromKeyAsync and any exact-key lookups.
    CONSTRAINT uq_node_tree_key UNIQUE (tree_id, key),

    -- Enforce ASCII-only flags
    CONSTRAINT chk_node_flags_ascii CHECK (flags ~ '^[\x00-\x7F]*$')
);

-- Trigram index on label_ix for all ILIKE '%pattern%' label searches:
--   • GetNodesAsync FilteredLabel filter
--   • MatchDescendants CTE: SELECT id FROM node WHERE label_ix ILIKE @filteredLabel
-- Replaces the former B-tree idx_node_label_ix, which was ineffective for
-- leading-wildcard patterns.
CREATE INDEX idx_node_label_ix_trgm ON node USING gin(label_ix gin_trgm_ops);

-- Trigram index on key for ILIKE '%pattern%' key searches:
--   • GetNodesAsync Key filter: n.key ILIKE @key
--   • GetNodesAsync ParentKey filter: p.key ILIKE @parentKey (join on parent)
--   • AncestorKey CTE base: WHERE key ILIKE @ancestorKey
-- Exact key lookups use uq_node_tree_key directly.
CREATE INDEX idx_node_key_trgm ON node USING gin(key gin_trgm_ops);

-- B-tree for flag equality or prefix queries. Note: flags LIKE '%f%' (single
-- character) cannot benefit from any index type, but this index has low overhead
-- and may assist future equality or range queries on flags.
CREATE INDEX idx_node_flags ON node(flags);

-- Covering composite index for tree + parent + key queries.
-- Replaces the former idx_node_tree_parent(tree_id, parent_id), which lacked the
-- key column needed to avoid a post-filter sort. Covers:
--   • GetRootNodes: WHERE tree_id=? AND parent_id IS NULL ORDER BY key
--   • GetNodesAsync with ParentId: WHERE tree_id=? AND parent_id=? ORDER BY key
--   • IsRoot filter: WHERE tree_id=? AND parent_id IS NULL [ORDER BY key]
--   • MatchDescendants main query: WHERE tree_id=? AND parent_id=? ORDER BY key
--   • GetNodePathAsync sibling count: WHERE tree_id=? AND parent_id=? AND key<=?
--     (range scan on the third column, returns pre-ordered results)
--   • tree_id alone (first column) used in bitmap AND with trigram scans
CREATE INDEX idx_node_tree_parent_key ON node(tree_id, parent_id, key);

-- Composite for parent-only queries (no tree_id filter). Covers:
--   • GetChildNodesAsync: WHERE parent_id=? ORDER BY key
--   • NodeHasChildrenAsync: WHERE parent_id=? (EXISTS / LIMIT 1)
--   • GetDescendantNodesAsync recursive: n.parent_id = d.id
--   • IsLeaf correlated EXISTS: WHERE c.parent_id = n.id
CREATE INDEX idx_node_parent_key ON node(parent_id, key);
