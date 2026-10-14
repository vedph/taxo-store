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
    note        VARCHAR(5000),

    -- True for flat trees (lists), whose nodes are all root nodes. Enforced
    -- by the store when writing trees and nodes.
    flat        BOOLEAN NOT NULL DEFAULT FALSE
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

-- ============================================
-- TABLE: node_mapping
-- Maps a node to equivalent entities in external resources (e.g. authority
-- lists like VIAF). When changing this table or tree.flat, also update the
-- upgrade script in PgSqlTaxoStore (UPGRADE_SQL), which brings databases
-- created by previous versions up to date.
-- ============================================

CREATE TABLE node_mapping (
    node_id     INT NOT NULL REFERENCES node(id) ON DELETE CASCADE,
    scope       VARCHAR(100) NOT NULL,
    target      VARCHAR(500) NOT NULL,
    label       VARCHAR(500),
    -- The filtered label, used for searching mappings (e.g. aliases) with
    -- the same criteria used for node.label_ix. Null when there is no label.
    label_ix    VARCHAR(500),

    -- A node cannot have the same target in the same scope more than once.
    -- This also serves as the index for fetching the mappings of nodes:
    -- WHERE node_id = ANY(...) ORDER BY node_id, scope, target.
    CONSTRAINT pk_node_mapping PRIMARY KEY (node_id, scope, target)
);

-- Reverse lookup: find the node(s) mapped to a target in a scope, or having
-- any mapping in a scope (GetNodesAsync MappingScope/MappingTarget filters).
CREATE INDEX idx_node_mapping_scope_target ON node_mapping(scope, target);

-- Trigram index on label_ix for ILIKE '%pattern%' mapping label searches
-- (GetNodesAsync FilteredMappingLabel filter, also in MatchDescendants CTE).
-- This yields the IDs of the matching nodes (node_id), which are then
-- joined to node via its primary key; when combined with FilteredLabel,
-- the two trigram scans (on node and node_mapping) are merged with UNION.
CREATE INDEX idx_node_mapping_label_ix_trgm ON node_mapping
    USING gin(label_ix gin_trgm_ops);
