# Taxonomies Store Nodes Part

🔑 `it.vedph.taxo-store-nodes`

- `treeId`\*: the identifier of the taxonomy tree used by this part. All the nodes in a taxonomy tree belong to it.
- `nodeIds` (`StringPair[]`):
  - `name` (`string`): the human-friendly name of the node. This might become stale during editing, but it is included for easier integration.
  - `value` (`string`): the node identifier, built with `treeId` + `/` + node's key.

This part is role-dependent, and it draws nodes from one specific tree (=taxonomy) at a time: just specify the tree ID of each part (with its role, if using multiple taxonomies) in backend settings.
