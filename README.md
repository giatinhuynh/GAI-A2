# Games and Artificial Intelligence Techniques
## COSC2527 / COSC3144 - Assignment 2
### Semester 1 2025

[![Review Assignment Due Date](https://classroom.github.com/assets/deadline-readme-button-22041afd0340ce965d47ae6ef1cefeee28c7c493a6346c4f15d667ab976d596c.svg)](https://classroom.github.com/a/gMgSDeGo)

**Student1 ID:** S3962053  
**Student1 Name:** Duc Gia Tin Huynh

---

## 🎮 Project Overview

This Unity project contains two distinct games showcasing various artificial intelligence techniques:

1. **🐸 Frog Game** - A survival game featuring advanced pathfinding and behavior trees
2. **🎯 Connect Four** - A strategic board game with multiple AI agents

---

## 🐸 Frog Game

### Game Description
A dynamic survival game where a frog must navigate through a complex environment, collect flies, avoid snakes, and survive various terrain challenges.

### 🚀 Key Features

#### **Advanced Pathfinding System**
- **A* Algorithm Implementation** with multiple heuristic options:
  - Manhattan Distance
  - Euclidean Distance  
  - Chebyshev Distance
  - Octile Distance
  - Custom Terrain-Aware Heuristic
- **Path Optimization**: Automatic path smoothing and waypoint reduction
- **Dynamic Path Recalculation**: Real-time path updates when obstacles change
- **Path Caching**: Performance optimization through intelligent caching
- **Terrain-Aware Movement**: Different movement costs for water, sand, and mud

#### **Behavior Tree AI System**
- **Modular Behavior Tree Framework** with comprehensive node types:
  - Sequence, Selector, Inverter, Repeater nodes
  - Conditional and Action nodes
  - Parallel execution support
  - Utility-based decision making
  - Random selection capabilities
- **Blackboard System**: Shared data management between AI components
- **Debug Visualization**: Real-time behavior tree state display

#### **Frog AI Behaviors**
- **Multi-Target Decision Making**: Evaluates flies based on distance, safety, and terrain
- **Threat Assessment**: Dynamic snake detection and avoidance
- **Terrain Adaptation**: Speed modifications based on surface type
- **Combat System**: Automatic bubble shooting at nearby snakes
- **Stuck Detection**: Intelligent path recalculation when immobilized

#### **Environmental Features**
- **Terrain Types**: Normal, Water, Sand, Mud with different movement costs
- **Dynamic Obstacles**: Moving objects that affect pathfinding
- **Screen Boundary Management**: Smart exploration limits
- **Visual Effects**: Terrain-based color changes and speed indicators

#### **Advanced AI Features**
- **Predictive Targeting**: Anticipates fly movement patterns
- **Safety Scoring**: Evaluates positions based on snake proximity
- **Smooth Transitions**: Behavior blending for natural movement
- **Performance Optimization**: Cooldown systems and efficient updates

---

## 🎯 Connect Four Game

### Game Description
A strategic implementation of Connect Four featuring multiple AI agents with different approaches to game theory and decision making.

### 🚀 Key Features

#### **Multiple AI Agents**

**1. Allis Agent (Expert Level)**
- **Knowledge-Based Strategy**: Implements Victor Allis's expert-level analysis
- **Threat Detection**: Identifies and responds to various threat types
- **Strategic Rules**: Implements 9 strategic rules (Claimeven, Baseinverse, Vertical, etc.)
- **Zugzwang Analysis**: Advanced positional control assessment
- **Transposition Tables**: Performance optimization through state caching

**2. Minimax Agent**
- **Alpha-Beta Pruning**: Optimized search algorithm
- **Iterative Deepening**: Progressive depth exploration
- **Zobrist Hashing**: Efficient state representation
- **Move Ordering**: Intelligent move prioritization
- **Time Management**: Configurable time limits

**3. Monte Carlo Tree Search (MCTS) Agent**
- **UCB1 Selection**: Balanced exploration vs exploitation
- **Simulation-Based Evaluation**: Random playout assessment
- **Configurable Parameters**: Adjustable simulation count and exploration constant
- **Tree Expansion**: Dynamic node creation

**4. Rule-Based Agent**
- **Heuristic Evaluation**: Position-based scoring
- **Immediate Threat Response**: Quick win/loss detection
- **Strategic Positioning**: Center control and blocking moves

**5. Random Agent**
- **Baseline Performance**: Random move selection for comparison

**6. Human Agent**
- **Player Control**: Manual gameplay interface

#### **Game Features**
- **Configurable Board Size**: 3-8 rows and columns
- **Flexible Win Conditions**: Adjustable pieces-to-connect (3-8)
- **Diagonal Support**: Optional diagonal win detection
- **Visual Feedback**: Animated piece dropping
- **Agent Switching**: Runtime agent selection

---

## 🛠️ Technical Implementation

### **Core Systems**

#### **Pathfinding Engine**
```csharp
// Advanced A* with multiple heuristics
public enum HeuristicType {
    Manhattan, Euclidean, Chebyshev, Octile, Custom
}
```

#### **Behavior Tree Framework**
```csharp
// Modular node system
public abstract class Node {
    public enum NodeState { Running, Success, Failure }
}
```

#### **AI Agent Architecture**
```csharp
// Extensible agent system
public abstract class Agent {
    public abstract int GetMove(Connect4State state);
}
```

### **Performance Optimizations**
- **Object Pooling**: Efficient memory management
- **Spatial Partitioning**: Optimized collision detection
- **Caching Systems**: Path and state caching
- **Cooldown Management**: Reduced computational overhead

### **Debug and Visualization**
- **Real-time Debug Info**: Behavior tree states, path visualization
- **Performance Metrics**: Frame rate, path calculation times
- **Visual Indicators**: Color-coded states, terrain effects

---

## 🎮 How to Play

### Frog Game
1. **Open Unity Hub** and load the project
2. **Navigate to Frog Game scenes**:
   - `FrogGame.unity` - Main game scene
   - `FrogGame Astar Vid 1.unity` - Pathfinding demonstration
   - `FrogGame Astar Vid 2.unity` - Advanced features showcase
3. **Control Modes**:
   - **Human Control**: Click to move frog
   - **AI Control**: Automatic behavior tree decision making
   - **Behavior Tree**: Advanced AI with multiple behaviors
4. **Objectives**:
   - Collect flies (10 required)
   - Avoid snakes
   - Navigate different terrain types
   - Survive as long as possible

### Connect Four Game
1. **Open Unity Hub** and load the project
2. **Navigate to Connect Four scene**: `Connect4.unity`
3. **Configure Agents**:
   - Select Game Controller object
   - Choose AI agents for Yellow and Red players
   - Adjust board settings (size, win conditions)
4. **Game Modes**:
   - **AI vs AI**: Watch different algorithms compete
   - **Human vs AI**: Play against various AI agents
   - **Human vs Human**: Manual gameplay

---

## 🔧 Configuration Options

### Frog Game Settings
- **Pathfinding**: Toggle A* vs direct movement
- **Heuristic Selection**: Choose from 5 different heuristics
- **Terrain Effects**: Enable/disable terrain speed modifications
- **Debug Visualization**: Show/hide path lines and behavior states
- **Behavior Tree**: Adjust decision weights and thresholds

### Connect Four Settings
- **Board Size**: 3-8 rows and columns
- **Win Condition**: 3-8 pieces to connect
- **Diagonal Wins**: Enable/disable diagonal connections
- **Time Limits**: Adjust AI thinking time
- **Search Depth**: Configure minimax search depth

---

## 📊 AI Performance Comparison

### Connect Four Agents (Expected Performance)
1. **Allis Agent**: Expert-level play (95%+ win rate)
2. **Minimax Agent**: Strong strategic play (80-90% win rate)
3. **MCTS Agent**: Good performance with time (70-85% win rate)
4. **Rule-Based Agent**: Decent play (60-75% win rate)
5. **Random Agent**: Baseline performance (25-35% win rate)

### Frog Game AI Features
- **Adaptive Decision Making**: Responds to changing environment
- **Multi-Objective Optimization**: Balances safety, efficiency, and goals
- **Predictive Behavior**: Anticipates threats and opportunities
- **Smooth Transitions**: Natural behavior changes

---

## 🎯 Key Improvements

### **Technical Enhancements**
- **Modular Architecture**: Extensible AI systems
- **Performance Optimization**: Efficient algorithms and caching
- **Debug Tools**: Comprehensive visualization and logging
- **Configurable Parameters**: Extensive customization options

### **AI Advancements**
- **Advanced Pathfinding**: Multiple heuristics and terrain awareness
- **Behavior Trees**: Complex decision-making systems
- **Game Theory**: Expert-level Connect Four strategies
- **Adaptive Learning**: Dynamic response to environment changes

### **User Experience**
- **Visual Feedback**: Clear indication of AI states and decisions
- **Real-time Debugging**: Live behavior tree and path visualization
- **Flexible Configuration**: Easy agent switching and parameter adjustment
- **Educational Value**: Demonstrates various AI techniques

---

## 🚀 Getting Started

### Prerequisites
- **Unity Version**: 6000.0.37f1 (as specified in project requirements)
- **Platform**: Windows, macOS, or Linux
- **Hardware**: Standard development machine capable of running Unity

### Installation
1. **Clone the repository**
2. **Open Unity Hub**
3. **Add the project** to Unity Hub
4. **Open the project** in Unity
5. **Select a scene** and press Play

### Troubleshooting
- **Unity Version**: Ensure you're using Unity 6000.0.37f1
- **Scene Loading**: If scenes don't load, check the Scenes folder
- **Agent Selection**: Verify agent prefabs are properly configured
- **Performance**: Adjust debug settings if experiencing frame rate issues

---

## 📝 Notes

- **Unity Version**: This project was created in Unity 6000.0.37f1. Please use the same version for compatibility.
- **Git Configuration**: Do not edit the contents of the .gitignore file.
- **Asset Organization**: All assets are properly organized in the Assets folder structure.
- **Documentation**: Comprehensive code comments and documentation throughout the project.

---

## 🎓 Educational Value

This project demonstrates:
- **Game AI Fundamentals**: Pathfinding, decision trees, game theory
- **Advanced Algorithms**: A*, Minimax, MCTS, Behavior Trees
- **Performance Optimization**: Caching, spatial partitioning, efficient algorithms
- **Software Architecture**: Modular design, extensible systems
- **Real-time Systems**: Dynamic decision making and adaptation

The implementation showcases both theoretical AI concepts and practical game development techniques, making it an excellent learning resource for AI and game development students.
