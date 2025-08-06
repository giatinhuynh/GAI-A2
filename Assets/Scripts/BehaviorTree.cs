//Reference: https://github.com/adammyhre/Unity-Behaviour-Trees
//Reference: https://www.youtube.com/watch?v=aR6wt5BlE-E
//Reference: https://www.youtube.com/watch?v=lusROFJ3_t8

using System.Collections.Generic;
using UnityEngine;

namespace BehaviorTreeSystem
{
    /// <summary>
    /// Possible states that a node can return
    /// </summary>
    public enum NodeState
    {
        Running,    // The node is currently executing its logic.
        Success,    // The node has successfully completed its execution.
        Failure     // The node has failed to complete its execution.
    }

    /// <summary>
    /// Blackboard system for sharing data between nodes in the behavior tree.
    /// Provides both global and type-specific data storage to prevent naming conflicts
    /// and allow for better organization of shared data.
    /// </summary>
    public class Blackboard
    {
        // Stores data accessible globally by any node using a string key.
        private Dictionary<string, object> _globalData = new Dictionary<string, object>();
        // Stores data scoped by type, preventing key collisions between different node types.
        private Dictionary<System.Type, Dictionary<string, object>> _typedData = new Dictionary<System.Type, Dictionary<string, object>>();

        /// <summary>
        /// Sets a value in the global blackboard data.
        /// </summary>
        public void SetValue<T>(string key, T value)
        {
            _globalData[key] = value;
        }
        
        /// <summary>
        /// Attempts to retrieve a value from the global blackboard data.
        /// </summary>
        /// <returns>True if the key exists and the value is of the correct type, false otherwise.</returns>
        public bool TryGetValue<T>(string key, out T value)
        {
            if (_globalData.TryGetValue(key, out object objValue) && objValue is T typedValue)
            {
                value = typedValue;
                return true;
            }
            
            value = default;
            return false;
        }
        
        /// <summary>
        /// Sets a value in the blackboard, scoped to a specific owner type.
        /// This helps prevent key conflicts between different systems using the blackboard.
        /// </summary>
        public void SetValueForType<TOwner, TValue>(string key, TValue value)
        {
            System.Type ownerType = typeof(TOwner);
            
            if (!_typedData.TryGetValue(ownerType, out var typeDict))
            {
                typeDict = new Dictionary<string, object>();
                _typedData[ownerType] = typeDict;
            }
            
            typeDict[key] = value;
        }
        
        /// <summary>
        /// Attempts to retrieve a type-specific value from the blackboard.
        /// </summary>
        /// <returns>True if the key exists for the owner type and the value is of the correct type, false otherwise.</returns>
        public bool TryGetValueForType<TOwner, TValue>(string key, out TValue value)
        {
            System.Type ownerType = typeof(TOwner);
            
            if (_typedData.TryGetValue(ownerType, out var typeDict) && 
                typeDict.TryGetValue(key, out object objValue) && 
                objValue is TValue typedValue)
            {
                value = typedValue;
                return true;
            }
            
            value = default;
            return false;
        }
        
        /// <summary>
        /// Clears all data from both global and type-specific storage.
        /// </summary>
        public void Clear()
        {
            _globalData.Clear();
            _typedData.Clear();
        }
    }

    /// <summary>
    /// Base class for all nodes in the behavior tree. Provides core functionality for:
    /// - Node hierarchy management (parent-child relationships)
    /// - State tracking and persistence between evaluations
    /// - Debug information and naming
    /// - Data sharing between nodes
    /// </summary>
    public abstract class Node
    {
        // The current state of the node (Running, Success, Failure). Updated during Evaluate().
        protected NodeState state;
        // Reference to the parent node in the tree hierarchy. Null for the root node.
        public Node parent;
        // List of child nodes attached to this node. Order matters for Sequence and Selector nodes.
        protected List<Node> children = new List<Node>();

        // A descriptive name for the node, primarily used for debugging purposes.
        protected string name;

        // --- Node Memory & State ---
        // Tracks if the node's OnInitialize method has been called since the last termination.
        // Ensures initialization logic runs only once per execution cycle.
        protected bool isInitialized = false;
        // Node-specific memory storage. Persists data only while the node is in the Running state.
        // Automatically cleared when the node terminates (Success or Failure).
        // Useful for storing temporary state like timers or counters within a node's execution cycle.
        protected Dictionary<string, object> _nodeMemory = new Dictionary<string, object>();

        // --- Legacy Data Sharing (Prefer Blackboard) ---
        // Older system for sharing data by propagating it up the tree.
        // Less flexible and more prone to issues than the Blackboard system.
        private Dictionary<string, object> _dataContext = new Dictionary<string, object>();

        // Reference to the BehaviorTree instance this node belongs to.
        // Used for accessing the Blackboard and tracking node status.
        protected BehaviorTree _behaviorTree;

        /// <summary>
        /// Constructor for a node without children.
        /// </summary>
        /// <param name="name">Optional name for debugging.</param>
        public Node(string name = "Node")
        {
            parent = null;
            this.name = name;
        }

        /// <summary>
        /// Constructor for a node with a list of children.
        /// </summary>
        /// <param name="children">List of child nodes to attach.</param>
        /// <param name="name">Optional name for debugging.</param>
        public Node(List<Node> children, string name = "Node")
        {
            this.name = name;
            foreach (Node child in children)
            {
                Attach(child);
            }
        }

        /// <summary>
        /// Attach a child node to this node
        /// </summary>
        /// <param name="node">The child node to attach.</param>
        public void Attach(Node node)
        {
            node.parent = this;
            children.Add(node);
            
            // Propagate behavior tree reference
            if (_behaviorTree != null)
            {
                node._behaviorTree = _behaviorTree;
            }
        }
        
        /// <summary>
        /// Sets the reference to the BehaviorTree instance for this node and recursively for all its children.
        /// This is typically called by the BehaviorTree constructor or when attaching nodes dynamically.
        /// </summary>
        /// <param name="tree">The BehaviorTree instance.</param>
        public void SetBehaviorTree(BehaviorTree tree)
        {
            _behaviorTree = tree;
            
            // Propagate to children
            foreach (var child in children)
            {
                child.SetBehaviorTree(tree);
            }
        }

        /// <summary>
        /// Evaluates the node's logic. This is the core execution method called by the parent node or the BehaviorTree.
        /// Handles the initialization and termination lifecycle (`OnInitialize`, `OnEvaluate`, `OnTerminate`).
        /// </summary>
        /// <returns>The resulting state of the node (Running, Success, or Failure) after evaluation.</returns>
        public virtual NodeState Evaluate()
        {
            if (!isInitialized)
            {
                OnInitialize();
                isInitialized = true;
            }
            
            state = OnEvaluate();
            
            // Track node state in behavior tree
            if (_behaviorTree != null)
            {
                _behaviorTree.TrackNodeStatus(this, state);
            }
            
            if (state != NodeState.Running)
            {
                OnTerminate(state);
                isInitialized = false;
            }
            
            return state;
        }
        
        /// <summary>
        /// Called the first time a node is evaluated or after it has terminated previously.
        /// Use this for initialization tasks like:
        /// - Resetting counters or timers
        /// - Setting up initial state
        /// - Preparing resources needed during evaluation
        /// Note: Will be called again after OnTerminate if node is re-evaluated
        /// </summary>
        protected virtual void OnInitialize() { }
        
        /// <summary>
        /// Abstract method where the core logic of the node resides.
        /// Must be implemented by derived classes (Sequence, Selector, Action, etc.).
        /// </summary>
        /// <returns>The state determined by the node's specific logic.</returns>
        protected abstract NodeState OnEvaluate();
        
        /// <summary>
        /// Called when a node finishes evaluation with success or failure.
        /// Use this for cleanup tasks like:
        /// - Releasing resources
        /// - Saving final state
        /// - Notifying other systems of completion
        /// Note: Not called while node remains in Running state
        /// </summary>
        /// <param name="finalState">The final state of the node (Success or Failure)</param>
        protected virtual void OnTerminate(NodeState finalState) { }
        
        /// <summary>
        /// Store a value in this node's memory for use across multiple evaluations.
        /// Memory persists while node is running but is cleared when node terminates.
        /// Useful for storing:
        /// - Timers for delayed actions
        /// - Target positions or entities
        /// - Progress tracking for multi-step tasks
        /// </summary>
        protected void SetMemory<T>(string key, T value)
        {
            _nodeMemory[key] = value;
        }
        
        /// <summary>
        /// Attempts to retrieve a value from this node's memory.
        /// </summary>
        /// <returns>True if the key exists and the value is of the correct type, false otherwise.</returns>
        protected bool GetMemory<T>(string key, out T value)
        {
            if (_nodeMemory.TryGetValue(key, out object objValue) && objValue is T typedValue)
            {
                value = typedValue;
                return true;
            }
            
            value = default;
            return false;
        }
        
        /// <summary>
        /// Clears all key-value pairs stored in this node's memory.
        /// Called automatically during OnTerminate.
        /// </summary>
        protected void ClearMemory()
        {
            _nodeMemory.Clear();
        }

        /// <summary>
        /// Sets data in the shared context (legacy method - prefer Blackboard).
        /// Propagates the data upwards to the parent node.
        /// </summary>
        /// <param name="key">The key identifying the data.</param>
        /// <param name="value">The data value to store.</param>
        public void SetData(string key, object value)
        {
            _dataContext[key] = value;

            if (parent != null)
            {
                parent.SetData(key, value);
            }
        }

        /// <summary>
        /// Gets data from the shared context (legacy method - prefer Blackboard).
        /// Searches upwards through parent nodes if the key is not found locally.
        /// </summary>
        /// <param name="key">The key identifying the data.</param>
        /// <param name="value">Output parameter where the retrieved value is stored.</param>
        /// <returns>True if the data was found in this node or any parent, false otherwise.</returns>
        public bool GetData(string key, out object value)
        {
            if (_dataContext.TryGetValue(key, out value))
            {
                return true;
            }

            if (parent != null)
            {
                return parent.GetData(key, out value);
            }

            value = null;
            return false;
        }

        /// <summary>
        /// Clears data from the shared context (legacy method - prefer Blackboard).
        /// Searches upwards through parent nodes to remove the key.
        /// </summary>
        /// <param name="key">The key identifying the data to clear.</param>
        /// <returns>True if the data was found and removed from this node or any parent, false otherwise.</returns>
        public bool ClearData(string key)
        {
            if (_dataContext.ContainsKey(key))
            {
                _dataContext.Remove(key);
                return true;
            }

            if (parent != null)
            {
                return parent.ClearData(key);
            }

            return false;
        }
        
        /// <summary>
        /// Returns the assigned name of this node.
        /// </summary>
        /// <returns>The node's name string.</returns>
        public string GetName()
        {
            return name;
        }
        
        /// <summary>
        /// Returns the list of direct child nodes attached to this node.
        /// </summary>
        /// <returns>A list of child nodes.</returns>
        public List<Node> GetChildren()
        {
            return children;
        }
    }

    /// <summary>
    /// The entry point of the behavior tree.
    /// It typically has only one child, which is the starting point of the tree's logic.
    /// </summary>
    public class RootNode : Node
    {
        public RootNode(string name = "Root") : base(name) { }
        
        public RootNode(Node child, string name = "Root") : base(new List<Node> { child }, name) { }

        /// Evaluates the single child node.
        /// Returns Failure if no child is attached.
        protected override NodeState OnEvaluate()
        {
            if (children.Count == 0)
            {
                return NodeState.Failure;
            }
            
            return children[0].Evaluate();
        }
    }

    /// <summary>
    /// Sequence node - executes children in order until one fails or all succeed.
    /// Similar to AND logic - all children must succeed for this node to succeed.
    /// Commonly used for sequences of actions that must all complete, like:
    /// - Approach target AND attack target
    /// - Find key AND open door AND enter room
    /// </summary>
    public class SequenceNode : Node
    {
        // Index of the child node currently being evaluated.
        private int currentChildIndex = 0;

        public SequenceNode(string name = "Sequence") : base(name) { }
        
        public SequenceNode(List<Node> children, string name = "Sequence") : base(children, name) { }
        
        /// Resets the execution state by setting the current child index back to 0.
        protected override void OnInitialize()
        {
            currentChildIndex = 0;
        }

        /// Executes children sequentially.
        /// Returns Success if all children succeed.
        /// Returns Failure immediately if any child fails.
        /// Returns Running if a child is Running.
        protected override NodeState OnEvaluate()
        {
            if (children.Count == 0)
            {
                return NodeState.Success;
            }
            
            // Continue from the current child
            while (currentChildIndex < children.Count)
            {
                Node child = children[currentChildIndex];
                NodeState childState = child.Evaluate();
                
                // If the child fails or is running, return the same state
                if (childState == NodeState.Failure)
                {
                    return NodeState.Failure;
                }
                
                if (childState == NodeState.Running)
                {
                    return NodeState.Running;
                }
                
                // Child succeeded, move to the next one
                currentChildIndex++;
            }
            
            // All children succeeded
            return NodeState.Success;
        }
    }

    /// <summary>
    /// Selector node - executes children in order until one succeeds or all fail.
    /// Similar to OR logic - only one child needs to succeed.
    /// Can operate in two modes:
    /// - Prioritized (default): Always starts from first child
    /// - Non-prioritized: Continues from last running child
    /// Commonly used for fallback behaviors, like:
    /// - Try primary attack OR try secondary attack OR retreat
    /// </summary>
    public class SelectorNode : Node
    {
        // Index of the child node currently being evaluated.
        private int currentChildIndex = 0;
        // Determines if the selector restarts from the first child (prioritized) or resumes from the last running child.
        private bool isPrioritized = true;

        public SelectorNode(string name = "Selector", bool prioritized = true) : base(name)
        { 
            isPrioritized = prioritized;
        }
        
        public SelectorNode(List<Node> children, string name = "Selector", bool prioritized = true) : base(children, name) 
        { 
            isPrioritized = prioritized;
        }
        
        /// Resets the execution state by setting the current child index back to 0.
        protected override void OnInitialize()
        {
            currentChildIndex = 0;
        }

        /// Executes children sequentially until one succeeds.
        /// Returns Success immediately if any child succeeds.
        /// Returns Failure if all children fail.
        /// Returns Running if a child is Running.
        /// Handles prioritized (always start from index 0) and non-prioritized (resume) execution.
        protected override NodeState OnEvaluate()
        {
            if (children.Count == 0)
            {
                return NodeState.Failure;
            }
            
            // In prioritized mode, always start from the first child
            if (isPrioritized)
            {
                currentChildIndex = 0;
            }
            
            // Continue from the current child
            while (currentChildIndex < children.Count)
            {
                Node child = children[currentChildIndex];
                NodeState childState = child.Evaluate();
                
                // If the child succeeds or is running, return the same state
                if (childState == NodeState.Success)
                {
                    return NodeState.Success;
                }
                
                if (childState == NodeState.Running)
                {
                    return NodeState.Running;
                }
                
                // Child failed, move to the next one
                currentChildIndex++;
            }
            
            // All children failed
            return NodeState.Failure;
        }
    }

    /// <summary>
    /// Inverter decorator - inverts the result of its child
    /// Success becomes Failure, Failure becomes Success. Running remains Running.
    /// Useful for conditions like "if NOT enemy visible".
    /// </summary>
    public class InverterNode : Node
    {
        public InverterNode(string name = "Inverter") : base(name) { }
        
        public InverterNode(Node child, string name = "Inverter") : base(new List<Node> { child }, name) { }

        /// Evaluates the child and inverts its result (Success/Failure).
        /// Returns Failure if no child is attached.
        protected override NodeState OnEvaluate()
        {
            if (children.Count == 0)
            {
                return NodeState.Failure;
            }

            switch (children[0].Evaluate())
            {
                case NodeState.Failure:
                    return NodeState.Success;
                case NodeState.Success:
                    return NodeState.Failure;
                case NodeState.Running:
                    return NodeState.Running;
                default:
                    return NodeState.Success;
            }
        }
    }

    /// <summary>
    /// Repeater decorator - repeats its child a number of times
    /// Executes its child node a specified number of times.
    /// Returns Success only after the child has succeeded the required number of times.
    /// Returns Failure immediately if the child fails at any point.
    /// Returns Running while the child is running or repetitions are incomplete.
    /// </summary>
    public class RepeaterNode : Node
    {
        // The total number of times the child should be repeated successfully.
        private int numRepeats;
        // Counter for the number of successful repetitions completed so far.
        private int count = 0;

        public RepeaterNode(int numRepeats, string name = "Repeater") : base(name)
        {
            this.numRepeats = numRepeats;
        }

        public RepeaterNode(Node child, int numRepeats, string name = "Repeater") : base(new List<Node> { child }, name)
        {
            this.numRepeats = numRepeats;
        }
        
        /// Resets the repetition counter.
        protected override void OnInitialize()
        {
            count = 0;
        }

        /// Evaluates the child node repeatedly.
        /// Tracks the number of successful executions.
        /// Returns Failure if no child is attached or if the child fails.
        protected override NodeState OnEvaluate()
        {
            if (children.Count == 0)
            {
                return NodeState.Failure;
            }

            if (count < numRepeats)
            {
                NodeState childState = children[0].Evaluate();
                
                if (childState == NodeState.Running)
                {
                    return NodeState.Running;
                }
                else if (childState == NodeState.Failure)
                {
                    return NodeState.Failure;
                }
                else  // Success
                {
                    count++;
                    if (count >= numRepeats)
                    {
                        return NodeState.Success;
                    }
                    return NodeState.Running;
                }
            }

            return NodeState.Success;
        }
    }

    /// <summary>
    /// Conditional node - checks a condition and returns success if true, failure if false
    /// Evaluates a boolean condition function.
    /// Returns Success if the condition is true, Failure otherwise.
    /// Does not execute any child nodes.
    /// </summary>
    public class ConditionalNode : Node
    {
        // The function delegate that encapsulates the condition to check.
        private System.Func<bool> condition;

        public ConditionalNode(System.Func<bool> condition, string name = "Condition") : base(name)
        {
            this.condition = condition;
        }

        /// Executes the condition function and returns Success or Failure based on the result.
        protected override NodeState OnEvaluate()
        {
            bool result = condition();
            return result ? NodeState.Success : NodeState.Failure;
        }
    }

    /// <summary>
    /// Action node - executes an action and returns its result
    /// Represents a leaf node that performs a specific action.
    /// The action is defined by a function delegate returning a NodeState.
    /// </summary>
    public class ActionNode : Node
    {
        // The function delegate that encapsulates the action to perform.
        private System.Func<NodeState> action;

        public ActionNode(System.Func<NodeState> action, string name = "Action") : base(name)
        {
            this.action = action;
        }

        /// Executes the action function and returns its resulting NodeState.
        protected override NodeState OnEvaluate()
        {
            return action();
        }
    }

    /// <summary>
    /// Parallel node - executes all children in parallel and manages their collective state.
    /// Two operation modes controlled by requireAllSuccess:
    /// - True (default): AND behavior, all must succeed (e.g., move while scanning)
    /// - False: OR behavior, one success is enough (e.g., multiple sensor checks)
    /// State management:
    /// - Returns Success when success conditions are met
    /// - Returns Failure if failure conditions are met
    /// - Returns Running if any child is still running and failure conditions not met
    /// Note: "Parallel" is logical parallel, all execution is still single-threaded
    /// </summary>
    public class ParallelNode : Node
    {
        // Policy: If true, all children must succeed for the parallel node to succeed.
        // If false, only one child needs to succeed.
        private bool requireAllSuccess;

        public ParallelNode(bool requireAllSuccess = true, string name = "Parallel") : base(name)
        {
            this.requireAllSuccess = requireAllSuccess;
        }
        
        public ParallelNode(List<Node> children, bool requireAllSuccess = true, string name = "Parallel") : base(children, name) 
        {
            this.requireAllSuccess = requireAllSuccess;
        }

        /// Evaluates all child nodes concurrently (logically).
        /// Determines the overall state based on the children's states and the `requireAllSuccess` policy.
        protected override NodeState OnEvaluate()
        {
            int successCount = 0;
            int failureCount = 0;
            
            foreach (Node child in children)
            {
                NodeState childState = child.Evaluate();
                
                if (childState == NodeState.Success)
                {
                    successCount++;
                }
                else if (childState == NodeState.Failure)
                {
                    failureCount++;
                }
            }
            
            // Return success if all children succeeded or if not requiring all success and at least one succeeded
            if ((requireAllSuccess && successCount == children.Count) || 
                (!requireAllSuccess && successCount > 0))
            {
                return NodeState.Success;
            }
            
            // Return failure if any child failed when requiring all success or all children failed otherwise
            if ((requireAllSuccess && failureCount > 0) ||
                (!requireAllSuccess && failureCount == children.Count))
            {
                return NodeState.Failure;
            }
            
            // Otherwise, at least one child is still running
            return NodeState.Running;
        }
    }

    /// <summary>
    /// Random selector - randomly selects one child to execute when initialized.
    /// Important characteristics:
    /// - Selection happens in OnInitialize, not every evaluation
    /// - Same child continues executing while in Running state
    /// - New random selection occurs after current child completes
    /// Useful for:
    /// - Unpredictable NPC behavior
    /// - Varied idle animations
    /// - Multiple equivalent options (e.g. patrol points)
    /// </summary>
    public class RandomSelectorNode : Node
    {
        // Index of the randomly selected child to execute. Determined during OnInitialize.
        private int currentChildIndex = -1;

        public RandomSelectorNode(string name = "RandomSelector") : base(name) { }
        
        public RandomSelectorNode(List<Node> children, string name = "RandomSelector") : base(children, name) { }
        
        /// Randomly selects one child index when the node is initialized.
        protected override void OnInitialize()
        {
            currentChildIndex = Random.Range(0, children.Count);
        }

        /// Evaluates the randomly selected child.
        /// Returns Failure if no children are attached.
        protected override NodeState OnEvaluate()
        {
            if (children.Count == 0)
            {
                return NodeState.Failure;
            }

            return children[currentChildIndex].Evaluate();
        }
    }
    
    /// <summary>
    /// Utility selector - selects the child with the highest utility score.
    /// Unlike simple selectors, this node uses scoring functions to make informed decisions.
    /// Each child has an associated utility function that returns a score (higher is better).
    /// Useful for decision making based on multiple factors, like:
    /// - Choosing between different targets based on distance, health, threat level
    /// - Selecting optimal positions based on cover, distance to objectives, enemy positions
    /// </summary>
    public class UtilitySelector : Node
    {
        // List of functions, each corresponding to a child, calculating its utility score.
        private List<System.Func<float>> utilityFunctions = new List<System.Func<float>>();
        // Index of the child with the highest utility score, selected during OnInitialize.
        private int currentChildIndex = -1;

        public UtilitySelector(string name = "UtilitySelector") : base(name) { }
        
        /// <summary>
        /// Attaches a child node along with its corresponding utility function.
        /// Ensures the order of children matches the order of utility functions.
        /// </summary>
        /// <param name="child">The child node to add.</param>
        /// <param name="utilityFunction">The function to calculate the child's utility score.</param>
        public void AddChild(Node child, System.Func<float> utilityFunction)
        {
            Attach(child);
            utilityFunctions.Add(utilityFunction);
        }
        
        /// Calculates the utility score for each child and selects the one with the highest score.
        /// Stores the index of the selected child.
        protected override void OnInitialize()
        {
            // Select the child with the highest utility score
            float highestScore = float.MinValue;
            currentChildIndex = 0;
            
            for (int i = 0; i < children.Count; i++)
            {
                if (i < utilityFunctions.Count)
                {
                    float score = utilityFunctions[i]();
                    if (score > highestScore)
                    {
                        highestScore = score;
                        currentChildIndex = i;
                    }
                }
            }
        }
        
        /// Evaluates the child node that was selected based on the highest utility score during OnInitialize.
        /// Returns Failure if no children are attached or if selection failed.
        protected override NodeState OnEvaluate()
        {
            if (children.Count == 0 || currentChildIndex < 0 || currentChildIndex >= children.Count)
            {
                return NodeState.Failure;
            }
            
            return children[currentChildIndex].Evaluate();
        }
    }
    
    /// <summary>
    /// Static utility node - executes an action with fixed utility score.
    /// The utility score is set once at creation and never changes.
    /// Best for actions whose relative priority is constant, like:
    /// - Emergency behaviors (always high priority)
    /// - Basic needs (eating, sleeping)
    /// - Fallback behaviors (low priority options)
    /// </summary>
    public class StaticUtilityNode : Node
    {
        // The action to perform if this node is selected by a UtilitySelector.
        private System.Func<NodeState> action;
        // The fixed utility score associated with this action.
        private float utilityScore;

        public StaticUtilityNode(System.Func<NodeState> action, float utilityScore, string name = "StaticUtility") : base(name)
        {
            this.action = action;
            this.utilityScore = utilityScore;
        }
        
        /// <summary>
        /// Returns the pre-defined, static utility score for this node.
        /// </summary>
        public float GetUtilityScore()
        {
            return utilityScore;
        }
        
        /// Executes the associated action when this node is evaluated (typically after being selected by a UtilitySelector).
        protected override NodeState OnEvaluate()
        {
            return action();
        }
    }
    
    /// <summary>
    /// Dynamic utility node - executes an action with dynamic utility score.
    /// Utility function is evaluated each time GetUtilityScore is called.
    /// Perfect for behaviors that depend on changing conditions:
    /// - Combat decisions based on health/ammo
    /// - Resource gathering based on inventory
    /// - Position selection based on enemy locations
    /// </summary>
    public class DynamicUtilityNode : Node
    {
        // The action to perform if this node is selected by a UtilitySelector.
        private System.Func<NodeState> action;
        // The function used to dynamically calculate the utility score each time it's needed.
        private System.Func<float> utilityFunction;

        public DynamicUtilityNode(System.Func<NodeState> action, System.Func<float> utilityFunction, string name = "DynamicUtility") : base(name)
        {
            this.action = action;
            this.utilityFunction = utilityFunction;
        }
        
        /// <summary>
        /// Calculates and returns the dynamic utility score by invoking the utility function.
        /// </summary>
        public float GetUtilityScore()
        {
            return utilityFunction();
        }
        
        /// Executes the associated action when this node is evaluated (typically after being selected by a UtilitySelector).
        protected override NodeState OnEvaluate()
        {
            return action();
        }
    }

    /// <summary>
    /// Behavior tree that manages the execution of nodes.
    /// Features:
    /// - Tree-wide data sharing through Blackboard
    /// - Debug mode for logging node execution
    /// - Active node tracking for visualization
    /// - Pause/resume functionality through SetActive
    /// Usage: Create instance with root node and update each frame
    /// Typical setup: behaviorTree.Update() called from MonoBehaviour's Update
    /// </summary>
    public class BehaviorTree
    {
        // The root node of the behavior tree structure.
        private RootNode root;
        // Flag to enable/disable the tree's execution. If false, Update() returns Failure.
        private bool isActive = true;
        // The shared data context for this behavior tree.
        private Blackboard blackboard;

        // --- Debugging & Tracking ---
        // Enables logging of node evaluations and state changes.
        private bool debugMode = false;
        // Stores log messages when debugMode is enabled.
        private List<string> debugLog = new List<string>();
        // List of nodes currently in the Running state. Useful for visualization or debugging.
        // Automatically managed via TrackNodeStatus called by nodes.
        private List<Node> activeNodes = new List<Node>();

        /// <summary>
        /// Constructor for the Behavior Tree.
        /// </summary>
        /// <param name="rootChild">The first child node to attach under the implicit RootNode.</param>
        /// <param name="enableDebug">Optional flag to enable debug logging from the start.</param>
        public BehaviorTree(Node rootChild, bool enableDebug = false)
        {
            root = new RootNode(rootChild);
            blackboard = new Blackboard();
            debugMode = enableDebug;
            
            // Set behavior tree reference in all nodes
            root.SetBehaviorTree(this);
        }
        
        /// <summary>
        /// Enables or disables the execution of the behavior tree.
        /// </summary>
        /// <param name="active">True to enable, false to disable.</param>
        public void SetActive(bool active)
        {
            isActive = active;
        }
        
        /// <summary>
        /// Provides access to the tree's shared Blackboard instance.
        /// </summary>
        /// <returns>The Blackboard object.</returns>
        public Blackboard GetBlackboard()
        {
            return blackboard;
        }
        
        /// <summary>
        /// Enables or disables the debug logging mode.
        /// </summary>
        /// <param name="enable">True to enable debug logging, false to disable.</param>
        public void SetDebugMode(bool enable)
        {
            debugMode = enable;
        }
        
        /// <summary>
        /// Clears all messages from the internal debug log.
        /// </summary>
        public void ClearDebugLog()
        {
            debugLog.Clear();
        }
        
        /// <summary>
        /// Retrieves the list of collected debug log messages.
        /// </summary>
        /// <returns>A list of strings containing debug information.</returns>
        public List<string> GetDebugLog()
        {
            return debugLog;
        }
        
        /// <summary>
        /// Internal method called by nodes to report their status changes.
        /// Updates the list of active nodes and adds log entries if debug mode is enabled.
        /// </summary>
        /// <param name="node">The node reporting its status.</param>
        /// <param name="state">The new state of the node.</param>
        public void TrackNodeStatus(Node node, NodeState state)
        {
            if (state == NodeState.Running)
            {
                if (!activeNodes.Contains(node))
                {
                    activeNodes.Add(node);
                }
                
                if (debugMode)
                {
                    debugLog.Add($"Active node: {node.GetName()} (Running)");
                }
            }
            else
            {
                activeNodes.Remove(node);
                
                if (debugMode)
                {
                    debugLog.Add($"Node completed: {node.GetName()} ({state})");
                }
            }
        }
        
        /// <summary>
        /// Retrieves the list of nodes currently in the 'Running' state.
        /// </summary>
        /// <returns>A list of active nodes.</returns>
        public List<Node> GetActiveNodes()
        {
            return activeNodes;
        }

        /// <summary>
        /// Executes a single tick (evaluation cycle) of the behavior tree, starting from the root node.
        /// Should typically be called once per frame or update cycle.
        /// </summary>
        /// <returns>The final state of the root node after the evaluation cycle (Success, Failure, or Running).</returns>
        public NodeState Update()
        {
            if (isActive)
            {
                if (debugMode)
                {
                    debugLog.Add("Updating behavior tree...");
                    // Clear active nodes at the start of each update
                    activeNodes.Clear();
                }
                
                NodeState result = root.Evaluate();
                
                if (debugMode)
                {
                    debugLog.Add($"Result: {result}");
                    debugLog.Add($"Active nodes: {activeNodes.Count}");
                }
                
                return result;
            }
            return NodeState.Failure;
        }
    }
} 