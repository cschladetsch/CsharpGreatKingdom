---
layout: default
title: Architecture
---

# Great Kingdom Architecture

## Project layout

```mermaid
graph LR
    Sol[GreatKingdom.sln]
    Sol --> Core[GreatKingdom<br/>rules + Raylib render]
    Sol --> Tests[GreatKingdom.Tests]
    Core --> Brains[brains<br/>AI opponents]
```
