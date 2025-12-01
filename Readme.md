# Great Kingdom (C# / Raylib)

**Great Kingdom** is a strategic territory-building game where a single captured stone results in immediate defeat ("Sudden Death"). This project is a C# implementation using **Raylib** for hardware-accelerated 2D graphics, specifically configured for Windows and WSL2 (Linux) environments.

![Status](https://img.shields.io/badge/Status-Playable-brightgreen)
![Tech](https://img.shields.io/badge/C%23-.NET%208.0-purple)
![Graphics](https://img.shields.io/badge/Raylib-v6.0-blue)

---

## Build & Run Instructions

### Prerequisites
* **.NET 8.0 SDK**
* **TorchSharp**: Neural network library (automatically installed via NuGet)
* **TorchSharp-cpu**: CPU-based PyTorch backend (automatically installed via NuGet)
* **Raylib-cs**: Hardware-accelerated graphics library (automatically installed via NuGet)
* **WSL2 / Linux Users:** You must install the underlying graphics and PyTorch dependencies.

### Install System Dependencies (WSL2 / Ubuntu)
*If you are on Windows (native), skip this step.*

```bash
sudo apt-get update
sudo apt-get install -y dotnet-sdk-8.0

# Raylib dependencies (windowing/input):
sudo apt-get install -y libasound2-dev libx11-dev libxrandr-dev libxi-dev \
libgl1-mesa-dev libglu1-mesa-dev libxcursor-dev libxinerama-dev libwayland-dev libxkbcommon-dev

# TorchSharp/LibTorch dependencies:
sudo apt-get install -y libgomp1
```

### Build the Game
Open your terminal in the **ProjectRoot** folder.

```bash
# 1. Restore NuGet packages (downloads Raylib-cs)
dotnet restore

# 2. Run the game
# Note: We point to the project folder explicitly
dotnet run --project GreatKingdom
```

---

## Game Rules

Great Kingdom combines the territory mechanics of *Go* with a "Sudden Death" rule.

See also [Rules](Rules.md).

### Core Mechanics
1.  **Objective:** Win via **Capture** (Sudden Death) or **Territory Scoring**.
2.  **Turn Order:** **Blue** moves first. **Orange** moves second.
3.  **The Board:** 9x9 Grid.
4.  **Neutral Castle:** A grey stone begins in the exact center (*Tengen*). It belongs to neither player but acts as a wall for territory.

### Victory Condition A: Sudden Death (Capture)
If you place a stone that removes the last liberty (adjacent empty space) of an enemy stone or group, **you win immediately**.
* **Double KO:** If a move surrounds *both* your stone and the opponent's stone, the **Active Player** (the one who moved) wins.
* **Suicide:** You cannot place a stone that has no liberties unless it captures the enemy. If you commit suicide without capturing, you lose.

### Victory Condition B: Territory Scoring
If both players **Pass** consecutively, the game ends. Players count the empty intersections fully enclosed by their stones.
* **Walls:** Board edges and the Neutral Castle count as walls.
* **The 4-Edge Rule:** A territory cannot touch **all four edges** of the board simultaneously.
* **Handicap:** Because Blue goes first, **Blue must win by 3 points**.
    * If `Blue Score >= Orange Score + 3`: **Blue Wins**.
    * Otherwise: **Orange Wins**.

---

## Neural Network AI System

Great Kingdom includes a Deep Q-Network (DQN) AI implementation using **TorchSharp**, capable of learning to play through self-training against the MCTS opponent.

### Architecture

The neural network is a fully-connected feedforward network:

```
Input Layer:  81 nodes (9x9 board state)
Hidden Layer: 256 nodes (ReLU activation)
Hidden Layer: 256 nodes (ReLU activation)
Output Layer: 81 nodes (Q-values for each board position)
```

**Input Encoding:**
* `1.0` = Your stones
* `-1.0` = Opponent stones
* `0.1` = Neutral castle
* `0.0` = Empty space

**Training Algorithm:**
* **Deep Q-Learning (DQN)** with experience replay and target network
* **Opponent**: MCTS (Monte Carlo Tree Search) with configurable iterations
* **Reward Shaping**: Captures (+0.3/stone), losses (-0.5/stone), win (+1.0), loss (-1.0)

### Brain Storage

Trained neural networks ("brains") are stored in the `brains/` directory at the project root.

**Filename Format:**
```
brain_L{loss}_G{games}_{timestamp}.bin
```

**Example:**
```
brain_L005843_G12345_20251202_143000.bin
  |      |      |         |
  |      |      |         └─ Timestamp (YYYYMMDD_HHMMSS)
  |      |      └─────────── Games played (12,345)
  |      └────────────────── Loss value * 1,000,000 (0.005843)
  └───────────────────────── Identifier prefix
```

**Special Files:**
* `latest.bin` - Alias pointing to the most recently saved brain

**Automatic Management:**
* System keeps the 5 best brains (lowest loss)
* Older/worse brains are automatically deleted
* Auto-save triggers when loss drops below `0.005`

### Training the Neural Net

1. **Launch Training Mode:** From the main menu, select "Train Neural Net"
2. **Training Loop:** The AI plays against MCTS continuously, learning from each game
3. **Monitor Progress:**
   * **Games Played**: Number of training episodes completed
   * **Current Loss**: Lower is better (target: < 0.005)
4. **Save Options:**
   * **Auto-Save**: Automatically saves when loss < 0.005
   * **Manual Save**: Press the "Save Brain" button
   * **Load Brain**: Select from previously saved brains

### Configuration

AI hyperparameters are defined in `ConfigData.cs`:

```csharp
// Learning
LearningRate: 0.000005    // How fast the network learns
Gamma: 0.85               // Discount factor for future rewards
BatchSize: 128            // Training batch size

// Exploration
EpsilonStart: 1.0         // Initial exploration rate (100%)
EpsilonMin: 0.05          // Minimum exploration rate (5%)
EpsilonDecay: 0.9995      // Exploration decay per game

// Memory
Capacity: 5000            // Experience replay buffer size
TargetUpdateFrequency: 500 // Update target network every N training steps
```

### Playing Against the Neural Net

1. From the main menu, select "VS Neural Net"
2. You play as **Blue**, the AI plays as **Orange**
3. The AI uses the most recently saved brain (`latest.bin`)
4. Training mode is disabled during gameplay for optimal performance

---

## Configuration File

The game uses `config.json` for runtime configuration. This file is located at the project root and is automatically loaded on startup.

### Location
```
CsharpGreatKingdom/
  config.json          # Main configuration file
  GreatKingdom/
    config.json        # Copy for development
```

### Full Configuration Structure

```json
{
  "Game": {
    "GridSize": 9,
    "MCTSIterations": 1000,
    "DefaultIP": "127.0.0.1",
    "Port": 7777
  },
  "AI": {
    "Hyperparameters": {
      "LearningRate": 0.0005,
      "Gamma": 0.95,
      "BatchSize": 128
    },
    "Exploration": {
      "EpsilonStart": 1.0,
      "EpsilonMin": 0.05,
      "EpsilonDecay": 0.9995
    },
    "Memory": {
      "Capacity": 10000,
      "TargetUpdateFrequency": 500
    }
  }
}
```

### Settings Explained

**Game Settings:**
| Parameter | Default | Description |
|-----------|---------|-------------|
| `GridSize` | 9 | Board dimensions (9x9 grid) |
| `MCTSIterations` | 1000 | Number of MCTS simulations for AI opponent (higher = stronger but slower) |
| `DefaultIP` | "127.0.0.1" | Default IP address for network games |
| `Port` | 7777 | Network port for multiplayer games |

**AI Hyperparameters:**
| Parameter | Default | Description |
|-----------|---------|-------------|
| `LearningRate` | 0.0005 | Neural network learning rate (lower = slower but more stable) |
| `Gamma` | 0.95 | Discount factor for future rewards (0.0-1.0, higher = more long-term thinking) |
| `BatchSize` | 128 | Number of experiences sampled per training iteration |

**AI Exploration:**
| Parameter | Default | Description |
|-----------|---------|-------------|
| `EpsilonStart` | 1.0 | Initial exploration rate (1.0 = 100% random moves at start) |
| `EpsilonMin` | 0.05 | Minimum exploration rate (always explore 5% to avoid local optima) |
| `EpsilonDecay` | 0.9995 | Exploration decay multiplier per game (closer to 1.0 = slower decay) |

**AI Memory:**
| Parameter | Default | Description |
|-----------|---------|-------------|
| `Capacity` | 10000 | Experience replay buffer size (more = better diversity, more RAM) |
| `TargetUpdateFrequency` | 500 | Update target network every N training steps (stabilizes learning) |

### Tuning Tips

**For Faster Training:**
* Increase `LearningRate` to 0.001-0.005
* Decrease `MCTSIterations` to 500-800
* Increase `EpsilonDecay` to 0.999

**For Better Final Performance:**
* Decrease `LearningRate` to 0.0001-0.0003
* Increase `MCTSIterations` to 1500-3000
* Increase `Capacity` to 20000-50000

**If Training is Unstable:**
* Decrease `LearningRate`
* Increase `Gamma` (0.95-0.99)
* Decrease `EpsilonDecay` (slower exploration reduction)

---

## Logic & Diagrams

### Game Loop Architecture
The State Machine handles the "Instant Win" checks immediately after placement.

```mermaid
graph TD
    Start((Start)) --> Init["Init Board<br/>Place Neutral Center"]
    Init --> Input{Player Input}
    
    Input -- Spacebar --> Pass[Pass Turn]
    Input -- Click --> Valid{Is Valid?}
    
    Valid -- No --> Input
    Valid -- Yes --> Place[Place Stone]
    
    Place --> Capture{"Enemy<br/>Captured?"}
    Capture -- YES --> WinA["INSTANT WIN<br/>(Sudden Death)"]
    
    Capture -- NO --> Suicide{"Self<br/>Captured?"}
    Suicide -- YES --> WinB["OPPONENT WINS<br/>(Suicide Rule)"]
    Suicide -- NO --> Switch[Switch Turn]
    
    Pass --> DoublePass{"2nd Consecutive<br/>Pass?"}
    DoublePass -- YES --> Score[Calculate Territory]
    DoublePass -- NO --> Switch
    
    Switch --> Input
```

### Territory Validation Algorithm (Flood Fill)
This logic determines if an empty area counts as points, is disputed, or is invalid.

```mermaid
graph TD
    Start([Flood Fill Empty Region]) --> Q1{Is region fully<br/>enclosed orthogonally?}
    Q1 -- No --> Invalid[Invalid: Open Gap]
    Q1 -- Yes --> Q2{Do borders contain<br/>Enemy Stones?}
    
    Q2 -- Yes --> Invalid[Invalid: Invaded/Disputed]
    Q2 -- No --> Q3{Does region touch<br/>ALL 4 Board Edges?}
    
    Q3 -- Yes --> Invalid[Invalid: 4-Edge Rule]
    Q3 -- No --> CheckOwner{Whose stones<br/>create the border?}
    
    CheckOwner -- Blue Only --> P1[Blue Territory]
    CheckOwner -- Orange Only --> P2[Orange Territory]
    CheckOwner -- Both Colors --> Neutral[Neutral/No Points]
```

---

## Controls

| Key | Action |
| :--- | :--- |
| **Left Mouse** | Place Stone |
| **Spacebar** | Pass Turn |
| **R** | Reset Game |
| **ESC** | Exit |

---

## Acknowledgements
The primary source material for the rules and mechanics implemented in this project is the official tutorial video by KBG Publishing.

Video Title: GREAT KINGDOM - How to Play

Publisher: KBG Publishing (Wiz Stone Series)

Link: https://www.youtube.com/watch?v=LcARX2S7a0c

The game is part of the Wiz Stone board game series, designed by professional Go player Lee Sedol.
