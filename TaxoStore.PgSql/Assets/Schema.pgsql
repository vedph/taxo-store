-- ============================================
-- TABLE: tree
-- ============================================

CREATE TABLE tree (
    id          VARCHAR(100) PRIMARY KEY,
    name        VARCHAR(500) NOT NULL,
    note        VARCHAR(5000)
);

-- Index on name for display/search purposes
CREATE INDEX idx_tree_name ON tree(name);

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

    -- Ensure key is unique within each tree
    CONSTRAINT uq_node_tree_key UNIQUE (tree_id, key),

    -- Enforce ASCII-only flags
    CONSTRAINT chk_node_flags_ascii CHECK (flags ~ '^[\x00-\x7F]*$')
);

-- Index for filtered label searches
CREATE INDEX idx_node_label_ix ON node(label_ix);

-- Index for flag-based queries
CREATE INDEX idx_node_flags ON node(flags);

-- Composite index for parent-child traversal within a tree
CREATE INDEX idx_node_tree_parent ON node(tree_id, parent_id);

-- Index for tree-based queries with key lookup
CREATE INDEX idx_node_tree_key ON node(tree_id, key);

-- Index for sibling position calculation (used by GetNodePath)
-- Optimizes counting siblings with same parent ordered by key
CREATE INDEX idx_node_parent_key ON node(parent_id, key);
