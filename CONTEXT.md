# CritCrit

CritCrit models brand organization trees and the operational data attached to nodes in those trees.

## Language

**Static Asset**:
A file attached to an org node that can be resolved locally or inherited by descendant nodes.
_Avoid_: Media, blob, upload

**Asset Key**:
A dot-separated identifier for a static asset slot, such as `kiosk.background-video`.
_Avoid_: Path, filename, config key

**Asset Group**:
The first segment of an asset key used to visually group assets in management screens.
_Avoid_: Folder, category

**Asset Entry**:
The local value for an asset key at a specific org node, either set to a file or explicitly unset.
_Avoid_: Asset version, blob record

**Effective Asset**:
The asset entry a node sees after applying nearest-ancestor inheritance and explicit unsets.
_Avoid_: Resolved blob, current file

## Relationships

- A **Static Asset** is identified by one **Asset Key**
- An **Asset Key** belongs to one derived **Asset Group**
- An **Asset Entry** belongs to exactly one org node
- An **Effective Asset** is produced from local and ancestor **Asset Entries**

## Example Dialogue

> **Dev:** "If the brand sets `kiosk.background-video`, does every store get it?"
> **Domain expert:** "Yes, unless a lower node has an **Asset Entry** that replaces it or explicitly unsets that **Asset Key**."

## Flagged Ambiguities

- "static assets" was used to mean both files and config-like node values — resolved: a **Static Asset** is a file-backed, node-resolved value, not a schema key or platform media library item.
