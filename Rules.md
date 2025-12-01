# Great Kingdom: Official Rulebook

**Great Kingdom** is a strategic abstract strategy game from the *Wiz Stone* series. It combines territory control mechanics with a "sudden death" capture rule.

---

## 1. Game Overview

* **Players:** 2 (Blue vs. Orange).
* **Board:** 9x9 Grid (typically).
* **Objective:** Achieve victory through **Sudden Death** (capturing an enemy castle) or **Territory Scoring** (controlling the most land).

---

## 2. Components & Setup

1.  **The Neutral Castle:** Place the grey Neutral Castle on the center intersection of the board (*Tengen*). This piece acts as a permanent, indestructible wall for both players.
2.  **Player Pieces:**
    * Player 1 takes **Blue Castles**.
    * Player 2 takes **Orange Castles**.
3.  **First Turn:** Blue always moves first.

---

## 3. Gameplay Loop

Players alternate turns placing one castle at a time. The game continues until a capture occurs or both players pass.

```mermaid
graph TD
    Start((Start Game)) --> BlueTurn[Blue Turn]
    
    subgraph Turn_Sequence
    direction TB
    Input{Player Action}
    Input -- Place Castle --> CheckCapture{Enemy Castle<br/>Surrounded?}
    Input -- Pass --> CheckDoublePass{2nd Consecutive<br/>Pass?}
    end

    BlueTurn --> Input
    OrangeTurn[Orange Turn] --> Input

    CheckCapture -- YES --> InstantWin[Active Player<br/>WINS IMMEDIATELY]
    CheckCapture -- NO --> EndTurn
    
    CheckDoublePass -- YES --> Scoring[End Game:<br/>Calculate Territory]
    CheckDoublePass -- NO --> EndTurn
    
    EndTurn -- Switch Player --> OrangeTurn
    EndTurn -- Switch Player --> BlueTurn
    
    Scoring --> Result((Final Result))
    InstantWin --> Result
```

---

## 4. Territory Rules

Territory is defined as empty intersections surrounded by a player's castles.

### 4.1. Definition of Enclosure
* **Orthogonal Only:** Connections must be Up, Down, Left, or Right. Diagonals do not count.
* **Walls:** You may use the **Board Edges** and the **Neutral Castle** as walls to complete an enclosure.

### 4.2. Invalid Territory
* **Invasion:** If an opponent's castle is inside the area, it is **not** territory.
* **The 4-Edge Rule:** A territory cannot touch all four edges of the board simultaneously.

### 4.3. Territory Logic Flow

```mermaid
graph TD
    Start([Analyze Potential Territory]) --> Q1{Is the area<br/>fully enclosed orthogonally?}
    Q1 -- No --> Invalid[INVALID: Open Gap]
    Q1 -- Yes --> Q2{Are the walls formed by<br/>YOUR pieces + Edges + Neutral?}
    
    Q2 -- No (Enemy walls) --> Invalid
    Q2 -- Yes --> Q3{Is there an Enemy Castle<br/>inside the area?}
    
    Q3 -- Yes --> Invalid[INVALID: Invaded]
    Q3 -- No --> Q4{Does the area touch<br/>ALL 4 board edges?}
    
    Q4 -- Yes --> Invalid[INVALID: 4-Edge Rule]
    Q4 -- No --> Valid[VALID TERRITORY<br/>Score = Count of Empty Points]
    
    style Valid fill:#4CAF50,stroke:#333,stroke-width:2px,color:white
    style Invalid fill:#F44336,stroke:#333,stroke-width:2px,color:white
```

---

## 5. Sudden Death (The Capture Rule)

Unlike traditional Go, **capturing a piece ends the game immediately.**

* **Surrounding:** A castle is surrounded when it has no orthogonal liberties (empty adjacent spots).
* **Double KO:** If placing a stone surrounds BOTH your stone and the opponent's stone simultaneously, the **Active Player** (the one who placed the stone) wins.

```mermaid
stateDiagram-v2
    [*] --> PlaceStone
    PlaceStone --> CheckLiberties
    
    state CheckLiberties {
        state "Is Enemy Surrounded?" as EnemyCheck
        state "Is Self Surrounded?" as SelfCheck
        
        [*] --> EnemyCheck
        [*] --> SelfCheck
    }
    
    CheckLiberties --> Outcome
    
    state Outcome {
        EnemyCheck --> Win : YES (Enemy Captured)
        SelfCheck --> Suicide : YES (Self Captured)
        
        state "Who Dies?" as WhoDies
        
        Win --> WhoDies
        Suicide --> WhoDies
        
        WhoDies --> ActivePlayerWins : Enemy Dies (Normal Win)
        WhoDies --> ActivePlayerWins : Both Die (Double KO Rule)
        WhoDies --> OpponentWins : Only Self Dies (Suicide)
    }
```

---

## 6. Scoring (The Handicap)

If no captures occur, the game ends when both players pass.

* **Blue Handicap:** Because Blue moves first, they have an advantage. To balance this, Blue must win by **3 or more points**.
* **Orange Advantage:** Orange wins ties and narrow losses (1 or 2 points).

**Winning Criteria:**
1.  Count empty spaces in Blue Territory ($B$).
2.  Count empty spaces in Orange Territory ($O$).
3.  Check: Is $B \ge O + 3$?

```mermaid
graph LR
    Count[Count Territories] --> B[Blue Score]
    Count --> O[Orange Score]
    
    B & O --> Compare{Calculate Difference<br/>(Blue - Orange)}
    
    Compare -- Difference is 3 or more --> BlueWin[BLUE WINS]
    Compare -- Difference is 0, 1, or 2 --> OrangeWin[ORANGE WINS]
    Compare -- Orange has more points --> OrangeWin
    
    style BlueWin fill:#2196F3,color:white
    style OrangeWin fill:#FF9800,color:white
```
