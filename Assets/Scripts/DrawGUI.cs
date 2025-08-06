using UnityEngine;
using System.Collections;
using UnityEngine.SceneManagement;

/// <summary>
/// Class responsible for drawing the game's immediate mode GUI elements.
/// This includes health, flies collected, game over screen, and various debug/info panels.
/// Uses the legacy OnGUI system.
/// </summary>
public class DrawGUI : MonoBehaviour
{
	// Sprites used for UI icons (assigned in Inspector)
	public Sprite HeartSprite;
	public Sprite FlySprite;

	// References to UI elements managed by the Canvas system (e.g., Game Over Panel)
	public GameObject GameOverPanel;
	public TMPro.TextMeshProUGUI GameOverText;
	public TMPro.TextMeshProUGUI RestartText;

	// --- GUI Layout Settings ---
	// Defines the visual properties for icons drawn via OnGUI.
	private int _iconSize = 20;         // Size of the heart/fly icons in pixels.
	private int _iconSeparation = 10;   // Horizontal separation between heart icons.

	// --- Internal State & References ---
	// Textures converted from the public Sprites for use with the legacy OnGUI rendering system.
	private Texture2D _heartTex;
	private Texture2D _flyTex;

	// Reference to the player character (Frog) to access its state (health, flies).
	private Frog _frog;

	// References to all snake enemies currently in the scene.
	private Snake[] _snakes;

	// Reference to the scene's pathfinding system component.
	private Pathfinding _pathfinding;

	// Cache previous game state values to detect changes frame-to-frame.
	private int _lastHealth = 3;                // Last known health value.
	private int _lastFliesCollected = 0;      // Last known fly count.

	// Tracks if the game has reached an end state (win or lose).
	private bool _isGameOver = false;

	// --- UI Panel Visibility Flags ---
	// Public boolean flags (editable in Inspector) to control which debug/info panels are displayed via OnGUI.
	public bool showHeuristicColors = false;      // If true, path lines are colored based on the heuristic used.
	public bool showHeuristicComparison = false;  // If true, paths for all heuristics are shown simultaneously for comparison.
	public bool showDecisionTreePanel = true;     // If true, shows the Frog's Decision Tree status panel (when active). Default: ON
	public bool showBehaviorTreePanel = true;     // If true, shows the Frog's Behavior Tree status panel (when active). Default: ON
	public bool showSnakeBehaviorTreePanel = true;// If true, shows the Snake's Behavior Tree status panel (when active). Default: ON
	public bool showHealthUI = true;              // If true, shows the player's health (hearts) and collected flies UI. Default: ON
	public bool showControlsPanel = true;         // If true, shows the main controls and keybinds help panel. Default: ON
	public bool showAStarPanel = true;            // If true, shows the basic A* settings panel (relevant when Frog uses A* movement). Default: ON
	public bool showSnakePanel = true;            // If true, shows the snake status panel (movement type, chase mode, control mode). Default: ON
	public bool showAStarAdvancedPanel = true;    // If true, shows the panel with advanced A* algorithm toggles. Default: ON
	public bool showTogglePanel = true;           // If true, shows the top bar with quick toggles for other panels. Default: ON

	// Tracks the time the last panel visibility was toggled (can be used for UI feedback like fading).
	private float _lastPanelToggleTime = 0f;

	/// <summary>
	/// Called once when the script instance is being loaded.
	/// Used for initialization, finding essential references, and setting up initial state.
	/// </summary>
	void Start()
	{
		// Convert the assigned Sprites to Texture2D format required by the OnGUI system.
		// Logs an error if sprites are not assigned in the Inspector.
		if (HeartSprite != null)
		{
			_heartTex = SpriteToTexture(HeartSprite);
		}
		else
		{
			// Log error if essential sprite is missing.
			Debug.LogError("Heart sprite is not assigned in the DrawGUI component!");
		}

		if (FlySprite != null)
		{
			_flyTex = SpriteToTexture(FlySprite);
		}
		else
		{
			// Log error if essential sprite is missing.
			Debug.LogError("Fly sprite is not assigned in the DrawGUI component!");
		}

		// Find the Frog GameObject in the scene and get its Frog component.
		GameObject frogObject = GameObject.Find("Frog");
		if (frogObject != null)
		{
			_frog = frogObject.GetComponent<Frog>();
			if (_frog == null)
			{
				// Log error if the Frog component is missing on the found object.
				Debug.LogError("Frog object found but it doesn't have a Frog component!");
			}
			else
			{
				// Initialize cached values with the frog's starting state.
				_lastHealth = _frog.Health;
				_lastFliesCollected = _frog.FliesCollected;
			}
		}
		else
		{
			// Log error if the Frog object itself cannot be found.
			Debug.LogError("Frog object not found in the scene!");
		}

		// Find all GameObjects currently in the scene that have a Snake component.
		_snakes = FindObjectsByType<Snake>(FindObjectsSortMode.None);
		// Log how many snakes were found initially.
		Debug.Log($"Found {_snakes.Length} snakes in the scene");

		// Log details about each found snake for debugging purposes.
		foreach (Snake snake in _snakes)
		{
			if (snake != null)
			{
				Debug.Log($"Found snake: {snake.name} - Movement: {snake.GetMovementTypeString()}");
			}
		}

		// Find the Pathfinding component instance in the scene.
		_pathfinding = FindObjectOfType<Pathfinding>();
		if (_pathfinding == null)
		{
			// Log a warning if the pathfinding system is not present (some features might not work).
			Debug.LogWarning("Pathfinding component not found in the scene!");
		}

		// Ensure the Game Over UI panel (managed by Canvas) is hidden at the start.
		if (GameOverPanel != null)
		{
			GameOverPanel.SetActive(false);
		}
	}

	/// <summary>
	/// Called once per frame.
	/// Handles input for toggling features/UI panels and checks for game state changes (win/lose conditions).
	/// </summary>
	void Update()
	{
		// Check for changes in the frog's state if the frog reference is valid.
		if (_frog != null)
		{
			// Check if health has changed since the last frame.
			if (_frog.Health != _lastHealth)
			{
				// Log the health change for debugging.
				Debug.Log("Health changed: " + _lastHealth + " -> " + _frog.Health);
				_lastHealth = _frog.Health; // Update the cached value.

				// Check for the death condition (health <= 0).
				if (_frog.Health <= 0 && !_isGameOver)
				{
					ShowGameOver(false); // Trigger the game over sequence (loss).
				}
			}

			// Check if the number of collected flies has changed.
			if (_frog.FliesCollected != _lastFliesCollected)
			{
				// Log the change in collected flies.
				Debug.Log("Flies collected changed: " + _lastFliesCollected + " -> " + _frog.FliesCollected);
				_lastFliesCollected = _frog.FliesCollected; // Update the cached value.

				// Check for the win condition (collected all flies). Assumes 10 is max based on original code.
				if (_frog.FliesCollected == 10 && !_isGameOver)
				{
					ShowGameOver(true); // Trigger the game over sequence (win).
				}
			}
		}

		// Periodically refresh the list of snakes.
		// This accounts for snakes potentially being spawned or destroyed during gameplay.
		// The check runs roughly once per second at 60 FPS.
		if (Time.frameCount % 60 == 0)
		{
			RefreshSnakeReferences();
		}

		// --- Input Handling for Global Toggles ---

		// [M] Key: Toggle movement type for all snakes simultaneously.
		if (Input.GetKeyDown(KeyCode.M) && _snakes != null)
		{
			Debug.Log($"M key pressed - toggling snake movement for {_snakes.Length} snakes");
			// Iterate through all known snakes and toggle their movement type.
			foreach (Snake snake in _snakes)
			{
				if (snake != null)
				{
					Debug.Log($"Toggling movement type for snake: {snake.name}");
					snake.ToggleMovementType();
				}
			}
		}

		// [C] Key: Toggle chase mode for all snakes simultaneously.
		if (Input.GetKeyDown(KeyCode.C) && _snakes != null)
		{
			Debug.Log($"C key pressed - toggling snake chase mode for {_snakes.Length} snakes");
			// Iterate through all known snakes and toggle their chase mode.
			foreach (Snake snake in _snakes)
			{
				if (snake != null)
				{
					Debug.Log($"Toggling chase mode for snake: {snake.name}");
					snake.ToggleChaseMode();
				}
			}
		}

		// [B] Key: Toggle control mode (e.g., FSM vs Behavior Tree) for all snakes.
		if (Input.GetKeyDown(KeyCode.B) && _snakes != null)
		{
			Debug.Log($"B key pressed - toggling snake control mode for {_snakes.Length} snakes");
			// Iterate through all known snakes and toggle their control mode.
			foreach (Snake snake in _snakes)
			{
				if (snake != null)
				{
					// Use try-catch as the method might not exist in older Snake script versions.
					try
					{
						Debug.Log($"Toggling control mode for snake: {snake.name}");
						snake.ToggleControlMode();
					}
					catch (System.Exception e)
					{
						// Log a warning if the toggle method is missing.
						Debug.LogWarning($"Could not toggle control mode for snake {snake.name}: {e.Message}");
					}
				}
			}
		}

		// [H] Key: Cycle through the available pathfinding heuristics in the Pathfinding system.
		if (Input.GetKeyDown(KeyCode.H) && _pathfinding != null)
		{
			CyclePathfindingHeuristic();
		}

		// [P] Key: Toggle dynamic path recalculation feature in the Pathfinding system.
		if (Input.GetKeyDown(KeyCode.P) && _pathfinding != null)
		{
			_pathfinding.ToggleDynamicPathRecalculation(!_pathfinding.enableDynamicPathRecalculation);
			Debug.Log("Dynamic path recalculation: " + (_pathfinding.enableDynamicPathRecalculation ? "Enabled" : "Disabled"));
		}

		// [U] Key: Toggle the frog's stuck detection behavior.
		if (Input.GetKeyDown(KeyCode.U) && _frog != null)
		{
			_frog.ToggleStuckDetection(!_frog.enableStuckDetection);
			Debug.Log("Frog unsticking: " + (_frog.enableStuckDetection ? "Enabled" : "Disabled"));
		}

		// [V] Key: Toggle periodic path updates for the frog (used in A* movement).
		if (Input.GetKeyDown(KeyCode.V) && _frog != null)
		{
			_frog.TogglePeriodicPathUpdates(!_frog.enablePeriodicPathUpdates);
			Debug.Log("Periodic path updates: " + (_frog.enablePeriodicPathUpdates ? "Enabled" : "Disabled"));
		}

		// [K] Key: Toggle the visualization of heuristic-colored path lines.
		if (Input.GetKeyDown(KeyCode.K))
		{
			ToggleHeuristicColors(!showHeuristicColors);
		}

		// [J] Key: Toggle the heuristic comparison mode (shows all paths at once).
		if (Input.GetKeyDown(KeyCode.J))
		{
			ToggleHeuristicComparison(!showHeuristicComparison);
		}

		// [G] Key: Toggle the visibility of the Frog Behavior Tree status panel.
		if (Input.GetKeyDown(KeyCode.G))
		{
			showBehaviorTreePanel = !showBehaviorTreePanel;
			_lastPanelToggleTime = Time.time; // Record toggle time.
		}

		// [L] Key: Toggle the visibility of the Snake Behavior Tree status panel.
		if (Input.GetKeyDown(KeyCode.L))
		{
			showSnakeBehaviorTreePanel = !showSnakeBehaviorTreePanel;
			_lastPanelToggleTime = Time.time; // Record toggle time.
		}

		// --- Input Handling for UI Panel Visibility Toggles (F-Keys) ---
		// [F1] Key: Toggle the Decision Tree panel.
		if (Input.GetKeyDown(KeyCode.F1))
		{
			showDecisionTreePanel = !showDecisionTreePanel;
			_lastPanelToggleTime = Time.time;
			Debug.Log("Decision Tree Panel: " + (showDecisionTreePanel ? "Visible" : "Hidden"));
		}

		// [F2] Key: Toggle the Health and Flies UI (hearts and fly count).
		if (Input.GetKeyDown(KeyCode.F2))
		{
			showHealthUI = !showHealthUI;
			_lastPanelToggleTime = Time.time;
			Debug.Log("Health UI: " + (showHealthUI ? "Visible" : "Hidden"));
		}

		// [F3] Key: Toggle the main Controls help panel.
		if (Input.GetKeyDown(KeyCode.F3))
		{
			showControlsPanel = !showControlsPanel;
			_lastPanelToggleTime = Time.time;
			Debug.Log("Controls Panel: " + (showControlsPanel ? "Visible" : "Hidden"));
		}

		// [F4] Key: Toggle the basic A* Settings panel.
		if (Input.GetKeyDown(KeyCode.F4))
		{
			showAStarPanel = !showAStarPanel;
			_lastPanelToggleTime = Time.time;
			Debug.Log("A* Settings Panel: " + (showAStarPanel ? "Visible" : "Hidden"));
		}

		// [F5] Key: Toggle the Snake Info panel.
		if (Input.GetKeyDown(KeyCode.F5))
		{
			showSnakePanel = !showSnakePanel;
			_lastPanelToggleTime = Time.time;
			Debug.Log("Snake Info Panel: " + (showSnakePanel ? "Visible" : "Hidden"));
		}

		// [F6] Key: Toggle the A* Advanced Controls panel.
		if (Input.GetKeyDown(KeyCode.F6))
		{
			showAStarAdvancedPanel = !showAStarAdvancedPanel;
			_lastPanelToggleTime = Time.time;
			Debug.Log("A* Advanced Controls: " + (showAStarAdvancedPanel ? "Visible" : "Hidden"));
		}

		// [F7] Key: Toggle the Toggle Panel itself (the top bar with indicators).
		if (Input.GetKeyDown(KeyCode.F7))
		{
			showTogglePanel = !showTogglePanel;
			_lastPanelToggleTime = Time.time;
			Debug.Log("Toggle Panel: " + (showTogglePanel ? "Visible" : "Hidden"));
		}

		// [F12] Key: Toggle visibility of specific UI panels simultaneously.
		// Note: This only affects the panels listed in the original code's logic.
		if (Input.GetKeyDown(KeyCode.F12))
		{
			// Check if the originally specified set of panels are all visible.
			bool allVisible = showDecisionTreePanel && showControlsPanel && showAStarPanel &&
							 showSnakePanel && showAStarAdvancedPanel;

			// Toggle the state of only those specific panels based on the 'allVisible' check.
			showDecisionTreePanel = !allVisible;
			showControlsPanel = !allVisible;
			showAStarPanel = !allVisible;
			showSnakePanel = !allVisible;
			showAStarAdvancedPanel = !allVisible;
			// Behavior Tree panels, Health UI, and Toggle Bar are NOT affected by this F12 logic.

			_lastPanelToggleTime = Time.time;
			Debug.Log("All UI Panels: " + (!allVisible ? "Visible" : "Hidden"));
		}

		// --- Input Handling for A* Algorithm Toggles ---
		// Need a reference to the A* grid for these toggles.
		AStarGrid astarGrid = null;
		if (_pathfinding != null)
		{
			astarGrid = Pathfinding.grid; // Access the static grid reference from Pathfinding.
		}

		// [D] Key: Toggle diagonal movement allowance in the A* grid.
		if (Input.GetKeyDown(KeyCode.D) && astarGrid != null)
		{
			astarGrid.ToggleDiagonalMovement(!astarGrid.includeDiagonalNeighbours);
			// Optional log: Debug.Log("Diagonal Movement: " + astarGrid.includeDiagonalNeighbours);
		}

		// [S] Key: Toggle path smoothing in the Pathfinding system.
		if (Input.GetKeyDown(KeyCode.S) && _pathfinding != null)
		{
			_pathfinding.TogglePathSmoothing(!_pathfinding.enablePathSmoothing);
			// Optional log: Debug.Log("Path Smoothing: " + _pathfinding.enablePathSmoothing);
		}

		// [O] Key: Toggle path optimization (redundant node removal) in Pathfinding.
		if (Input.GetKeyDown(KeyCode.O) && _pathfinding != null)
		{
			_pathfinding.TogglePathOptimization(!_pathfinding.enablePathOptimization);
			// Optional log: Debug.Log("Path Optimization: " + _pathfinding.enablePathOptimization);
		}

		// [Y] Key: Toggle dynamic obstacle detection in the A* grid.
		if (Input.GetKeyDown(KeyCode.Y) && astarGrid != null)
		{
			astarGrid.ToggleDynamicObstacles(!astarGrid.enableDynamicObstacles);
			// Optional log: Debug.Log("Dynamic Obstacles: " + astarGrid.enableDynamicObstacles);
		}

		// [W] Key: Toggle the use of weighted nodes (terrain costs) in the A* grid.
		if (Input.GetKeyDown(KeyCode.W) && astarGrid != null)
		{
			astarGrid.ToggleWeightedNodes(!astarGrid.enableWeightedNodes);
			// Optional log: Debug.Log("Weighted Nodes: " + astarGrid.enableWeightedNodes);
		}

		// [G] Key (also used for Frog BT Panel): Toggle periodic grid updates in the A* grid.
		// This key is overloaded. The check for showAStarAdvancedPanel helps disambiguate,
		// assuming the user intends to toggle grid updates when that panel is visible.
		if (Input.GetKeyDown(KeyCode.G) && astarGrid != null)
		{
			// Only toggle grid update if the relevant A* panel is shown.
			if (showAStarAdvancedPanel)
            {
                astarGrid.TogglePeriodicGridUpdate(!astarGrid.enablePeriodicGridUpdate);
                // Optional log: Debug.Log("Periodic Grid Update: " + astarGrid.enablePeriodicGridUpdate);
            }
            // Otherwise, the other G key handler (for the Frog BT panel) might take effect if that panel is active.
		}

		// [N] Key: Toggle corner avoidance logic in the Pathfinding system.
		if (Input.GetKeyDown(KeyCode.N) && _pathfinding != null)
		{
			_pathfinding.ToggleCornerAvoidance(!_pathfinding.enableCornerAvoidance);
			// Optional log: Debug.Log("Corner Avoidance: " + _pathfinding.enableCornerAvoidance);
		}
	}

	/// <summary>
	/// Enables or disables the visualization of path lines colored by heuristic type.
	/// Updates the internal flag `showHeuristicColors`.
	/// </summary>
	/// <param name="enable">True to enable color visualization, false to disable.</param>
	public void ToggleHeuristicColors(bool enable)
	{
		showHeuristicColors = enable;
		Debug.Log("Heuristic color visualization: " + (showHeuristicColors ? "Enabled" : "Disabled"));
		// Note: The actual drawing of colored lines happens elsewhere (e.g., in Frog or Pathfinding Gizmos/Debug).
	}

	/// <summary>
	/// Enables or disables the heuristic comparison mode, which shows paths for all heuristics simultaneously.
	/// Automatically enables color visualization if comparison is turned on.
	/// Notifies the Frog component to update its visualization state.
	/// </summary>
	/// <param name="enable">True to enable comparison mode, false to disable.</param>
	public void ToggleHeuristicComparison(bool enable)
	{
		showHeuristicComparison = enable;

		// Comparison mode requires color visualization to be meaningful.
		if (enable && !showHeuristicColors)
		{
			showHeuristicColors = true;
		}

		// Notify the Frog component about the change in comparison mode.
		if (_frog != null)
		{
			_frog.SetHeuristicComparisonMode(enable); // Frog handles drawing the comparison paths.
		}

		Debug.Log("Heuristic comparison mode: " + (showHeuristicComparison ? "Enabled" : "Disabled"));
	}

	/// <summary>
	/// Cycles to the next pathfinding heuristic available in the `HeuristicType` enum.
	/// Updates the `heuristicType` in the Pathfinding component.
	/// </summary>
	private void CyclePathfindingHeuristic()
	{
		if (_pathfinding == null) return; // Safety check: requires pathfinding component.

		// Get the current heuristic type.
		HeuristicType currentHeuristic = _pathfinding.heuristicType;

		// Cycle to next heuristic
		int nextHeuristicIndex = ((int)currentHeuristic + 1) % System.Enum.GetValues(typeof(HeuristicType)).Length;
		HeuristicType nextHeuristic = (HeuristicType)nextHeuristicIndex;

		// Apply the newly selected heuristic to the Pathfinding system.
		_pathfinding.SetHeuristicType(nextHeuristic);

		// Log the change for confirmation.
		Debug.Log($"Changed pathfinding heuristic from {currentHeuristic} to {nextHeuristic}");
	}

	/// <summary>
	/// Gets the display name of the currently selected pathfinding heuristic,
	/// formatted with rich text color tags for visual distinction in the UI.
	/// </summary>
	/// <returns>A string containing the colored heuristic name (e.g., "<color=#CC3333>Manhattan</color>"), or "N/A" if pathfinding is unavailable.</returns>
	private string GetCurrentHeuristicColoredName()
	{
		if (_pathfinding == null) return "";

		// Get the enum value's name as a string.
		string name = _pathfinding.heuristicType.ToString();

		// Determine the appropriate color hex code based on the heuristic type.
		string colorHex = "#FFFFFF"; // Default to white.
		switch (_pathfinding.heuristicType)
		{
			case HeuristicType.Manhattan: colorHex = "#CC3333"; break; // Reddish
			case HeuristicType.Euclidean: colorHex = "#33CC33"; break; // Greenish
			case HeuristicType.Chebyshev: colorHex = "#3333CC"; break; // Bluish
			case HeuristicType.Octile:    colorHex = "#CCCC33"; break; // Yellowish
			case HeuristicType.Custom:    colorHex = "#CC33CC"; break; // Magenta
		}

		// Return the name wrapped in HTML-like color tags for OnGUI rich text.
		return $"<color={colorHex}>{name}</color>";
	}

	/// <summary>
	/// Refreshes the internal array of snake references (`_snakes`) by re-querying the scene.
	/// Called periodically to ensure the list is up-to-date if snakes are dynamically added or removed.
	/// </summary>
	private void RefreshSnakeReferences()
	{
		// Find all current Snake objects in the scene.
		Snake[] newSnakes = FindObjectsByType<Snake>(FindObjectsSortMode.None);

		// Only update and log if the number of snakes has actually changed.
		if (newSnakes.Length != _snakes.Length)
		{
			Debug.Log($"Snake count changed from {_snakes.Length} to {newSnakes.Length}");
			_snakes = newSnakes;
		}
	}

	/// <summary>
	/// Activates the Game Over UI Panel (Canvas element) and sets the appropriate win/lose message.
	/// Sets the `_isGameOver` flag to true.
	/// </summary>
	/// <param name="isWin">True if the player won, false if they lost.</param>
	private void ShowGameOver(bool isWin)
	{
		_isGameOver = true; // Mark the game as over.

		// Ensure the references to the Game Over UI elements are valid.
		if (GameOverPanel != null && GameOverText != null)
		{
			// Activate the main panel.
			GameOverPanel.SetActive(true);
			// Set the main message text based on win/loss condition.
			GameOverText.text = isWin ? "You won!" : "You died!";

			// Set the restart prompt text if the reference exists.
			if (RestartText != null)
			{
				RestartText.text = "Press R to Restart";
			}
		}
	}

	// Update control mode display text
	string GetControlModeText(Frog.ControlMode mode)
	{
		switch (mode)
		{
			case Frog.ControlMode.Human: return "Human";
			case Frog.ControlMode.AI: return "Decision Tree"; // Assuming AI enum means Decision Tree
			case Frog.ControlMode.BehaviorTree: return "Behavior Tree";
			default: return "Unknown"; // Fallback for unexpected values
		}
	}

	/// <summary>
	/// Draws a legend box explaining the color coding used for different heuristic path visualizations.
	/// Only drawn via OnGUI if `showHeuristicColors` and `showControlsPanel` are true.
	/// </summary>
	private void DrawHeuristicLegend()
	{
		// Check conditions for drawing the legend.
		if (!showHeuristicColors || _pathfinding == null || !showControlsPanel) return;

		// Define the style for the legend box using default GUI skin elements.
		GUIStyle legendStyle = new GUIStyle(GUI.skin.box);
		legendStyle.fontSize = 12;
		legendStyle.normal.textColor = Color.white;
		legendStyle.padding = new RectOffset(10, 10, 10, 10);

		// Build the text content for the legend. Use rich text for colors.
		string legendText = showHeuristicComparison ?
			"Heuristic Comparison (All Paths Shown):" : 
			"Heuristic Color Legend:";

		legendText += "\n<color=#CC3333>■</color> Manhattan: Sum of X and Y distances";
		legendText += "\n<color=#33CC33>■</color> Euclidean: Direct 'as-the-crow-flies' distance";
		legendText += "\n<color=#3333CC>■</color> Chebyshev: Max of X and Y distances";
		legendText += "\n<color=#CCCC33>■</color> Octile: Considers diagonal movement costs";
		legendText += "\n<color=#CC33CC>■</color> Custom: Weighted by terrain types";

		// Add instructions for toggling comparison mode.
		if (showHeuristicComparison)
		{
			legendText += "\n\nPress [J] to exit comparison mode";
		}
		else
		{
			legendText += "\n\nPress [J] to compare all heuristics";
		}

		// Add panel toggle info
		legendText += "\nPress [F1-F5] to toggle UI panels, [F12] for all";

		// Calculate the size needed for the legend box based on its content.
		Vector2 legendSize = legendStyle.CalcSize(new GUIContent(legendText));
		// Position the legend box at the top center of the screen.
		Rect legendRect = new Rect(
			Screen.width / 2 - legendSize.x / 2, // Center horizontally.
			10,                                  // Offset from the top edge.
			legendSize.x,
			legendSize.y
		);

		// Draw the legend box using OnGUI.
		GUI.Box(legendRect, legendText, legendStyle);
	}

	/// <summary>
	/// Draws a more detailed explanation panel describing the characteristics of each pathfinding heuristic.
	/// Only shown via OnGUI when `showHeuristicComparison` and `showControlsPanel` are true.
	/// </summary>
	private void DrawHeuristicExplanationPanel()
	{
		// Check conditions for drawing the panel.
		if (!showHeuristicComparison || !showControlsPanel) return;

		// Define the style for the explanation panel box.
		GUIStyle panelStyle = new GUIStyle(GUI.skin.box);
		panelStyle.fontSize = 10;
		panelStyle.normal.textColor = Color.white;
		panelStyle.padding = new RectOffset(15, 15, 15, 15);
		panelStyle.wordWrap = true;
		
		// Create the explanation content
		string explanationText = "Heuristic Comparison: Key Differences";

		explanationText += "\n\n<color=#CC3333>Manhattan</color>: Favors grid-aligned movements. Often creates paths with right angles.";
		explanationText += " Best for grid-based games where movement is restricted to cardinal directions.";
		
		explanationText += "\n\n<color=#33CC33>Euclidean</color>: Calculates straight-line distance. Produces more direct paths.";
		explanationText += " Best for open spaces where movement in any direction is possible.";
		
		explanationText += "\n\n<color=#3333CC>Chebyshev</color>: Allows diagonal movement at the same cost as cardinal directions.";
		explanationText += " Can result in more diagonal paths compared to Manhattan.";
		
		explanationText += "\n\n<color=#CCCC33>Octile</color>: Balances between Manhattan and Euclidean by accounting for the higher";
		explanationText += " cost of diagonal movement (typically 1.414×). Great for games that allow diagonal movement.";
		
		explanationText += "\n\n<color=#CC33CC>Custom</color>: Incorporates terrain costs into the heuristic calculation.";
		explanationText += " Results in paths that consider terrain difficulties from the planning stage.";

		// Calculate size and position (middle left side of screen)
		Vector2 explanationSize = panelStyle.CalcSize(new GUIContent(explanationText));
		explanationSize.x = 275; // Fix width to allow proper wrapping
		explanationSize.y = 200; // Adjusted height for more content

		Rect explanationRect = new Rect(
			10, // Left side margin
			(Screen.height - explanationSize.y) / 2, // Center vertically
			explanationSize.x,
			explanationSize.y
		);

		// Draw the explanation panel box using OnGUI.
		GUI.Box(explanationRect, explanationText, panelStyle);
	}


	/// <summary>
	/// Draws a panel displaying the current state and relevant data for the Frog's Decision Tree AI.
	/// Only drawn via OnGUI if the frog is using `Frog.ControlMode.AI` and `showDecisionTreePanel` is true.
	/// </summary>
	private void DrawFrogDecisionTreeState()
	{
		// Check conditions for drawing the panel.
		if (_frog == null || _frog.controlMode != Frog.ControlMode.AI || !showDecisionTreePanel) return;

		// Define the style for the decision tree status panel.
		GUIStyle panelStyle = new GUIStyle(GUI.skin.box);
		panelStyle.fontSize = 12;
		panelStyle.normal.textColor = Color.white;
		panelStyle.padding = new RectOffset(15, 15, 15, 15);
		panelStyle.wordWrap = true;

		// Create the panel content
		string panelText = "<b><color=#8AFF8A>Frog Decision Tree Status</color></b>";

		// Add current state
		string currentState = _frog._currentBehaviorState;
		Color stateColor = Color.white;
		
		switch (currentState)
		{
			case "Dead": stateColor = new Color(1f, 0.2f, 0.2f); break; // Red
			case "Freeze": stateColor = new Color(0.5f, 0.5f, 1f); break; // Blue
			case "Fleeing": stateColor = new Color(1f, 0.5f, 0.3f); break; // Orange
			case "Hunting": stateColor = new Color(0.3f, 1f, 0.3f); break; // Green
			case "Returning": stateColor = new Color(1f, 1f, 0.3f); break; // Yellow
			case "Center": stateColor = new Color(0.7f, 0.7f, 1f); break; // Light Blue
			case "User Override": stateColor = new Color(1f, 0.7f, 1f); break; // Pink
		}
		
		string colorHex = ColorUtility.ToHtmlStringRGB(stateColor);
		panelText += $"\n\n<b>Current State: <color=#{colorHex}>{currentState}</color></b>";

		// Add frog stats and environment information
		panelText += $"\n\nHealth: {_frog.Health}  |  Flies Collected: {_frog.FliesCollected}/{Frog.MaxFlies}";

		// Display current terrain information and speed modifier.
		TerrainType terrain = _frog.GetCurrentTerrain();
		float speedMod = _frog.GetTerrainModifiedSpeed() / _frog.MaxSpeed;
		string terrainColor = "#FFFFFF";
		
		switch (terrain)
		{
			case TerrainType.Normal:
				terrainColor = "#FFFFFF";
				break;
			case TerrainType.Water:
				terrainColor = "#00FFFF";
				break;
			case TerrainType.Sand:
				terrainColor = "#FFFF00";
				break;
			case TerrainType.Mud:
				terrainColor = "#CC6600";
				break;
		}
		
		panelText += $"\nTerrain: <color={terrainColor}>{terrain}</color> | Speed: {(speedMod * 100).ToString("F0")}%";

		// Add information about targets and threats
		if (_frog.closestFly != null)
		{
			float distToFly = _frog.distanceToClosestFly;
			panelText += $"\n\n<color=#88FF88>Nearest Fly:</color> {distToFly.ToString("F1")} units away";
			
			if (_frog._isHuntingFly)
			{
				panelText += " (Hunting)";
			}
			else if (distToFly > _frog.huntRange)
			{
				panelText += " (Out of range)";
			}
		}
		else
		{
			panelText += "\n<color=#88FF88>No flies detected</color>";
		}

		// Display information about the nearest snake threat.
		if (_frog.closestSnake != null)
		{
			float distToSnake = _frog.distanceToClosestSnake;
			string snakeText = $"\n<color=#FF8888>Nearest Snake:</color> {distToSnake.ToString("F1")} units away";
			
			if (distToSnake < _frog.scaredRange)
			{
				snakeText += " <b>(DANGER!)</b>";
			}
			
			panelText += snakeText;

			// Add info about nearby snake count
			int snakeCount = _frog.nearbySnakes.Count;
			if (snakeCount > 1)
			{
				panelText += $"\n<color=#FF8888>Multiple snakes nearby ({snakeCount})!</color>";
			}
		}
		else
		{
			panelText += "\n<color=#88FF88>No snakes nearby</color>"; // Use green if safe.
		}

		// Add decision information
		panelText += $"\n\n<b>Last Decision:</b> {_frog._lastDecision}";

		// Add screen position info
		bool offScreen = _frog.isOutOfScreen(_frog.transform);
		if (offScreen)
		{
			panelText += "\n\n<color=#FFCC00>Frog is outside screen bounds</color>";
		}

		// Calculate size and position
		Vector2 panelSize = panelStyle.CalcSize(new GUIContent(panelText));
		panelSize.x = 300; // Fixed width for better formatting
		panelSize.y = 200; // Fixed height to accommodate all content

		Rect panelRect = new Rect(
			10, // Left side margin
			(Screen.height - panelSize.y) / 2, // Center vertically
			panelSize.x,
			panelSize.y
		);

		// Draw the Decision Tree status panel using OnGUI.
		GUI.Box(panelRect, panelText, panelStyle);
	}


	/// <summary>
	/// Draws a panel displaying the current state and relevant data for the Frog's Behavior Tree AI.
	/// Only drawn via OnGUI if the frog is using `Frog.ControlMode.BehaviorTree` and `showBehaviorTreePanel` is true.
	/// </summary>
	private void DrawFrogBehaviorTreeState()
	{
		// Check conditions for drawing and get the BT component reference.
		if (_frog == null || _frog.controlMode != Frog.ControlMode.BehaviorTree || !showBehaviorTreePanel) return;
		FrogBehaviorTree frogBT = _frog.GetComponent<FrogBehaviorTree>();
		if (frogBT == null) return; // Need the BT component to get its state.

		// Define the style for the behavior tree status panel.
		GUIStyle panelStyle = new GUIStyle(GUI.skin.box);
		panelStyle.fontSize = 12;
		panelStyle.normal.textColor = Color.white;
		panelStyle.padding = new RectOffset(15, 15, 15, 15);
		panelStyle.wordWrap = true;

		// Create the panel content
		string panelText = "<b><color=#8ABBFF>Frog Behavior Tree Status</color></b>";

		// Add current state - get from behavior tree
		string currentState = frogBT.CurrentBehaviorName;
		Color stateColor = Color.white;
		
		switch (currentState)
		{
			case "Dead": stateColor = new Color(1f, 0.2f, 0.2f); break; // Red
			case "Return to Screen": stateColor = new Color(0.3f, 0.8f, 1f); break; // Light Blue
			case "Freeze (Multiple Snakes)": stateColor = new Color(0.5f, 0.5f, 1f); break; // Blue
			case "Flee from Snake": stateColor = new Color(1f, 0.5f, 0.3f); break; // Orange
			case "Hunting Fly": stateColor = new Color(0.3f, 1f, 0.3f); break; // Green
			case "Center Wandering": stateColor = new Color(0.7f, 0.7f, 1f); break; // Light Purple/Blue
			case "Following User Target": stateColor = new Color(1f, 0.7f, 1f); break; // Pink
		}
		
		string colorHex = ColorUtility.ToHtmlStringRGB(stateColor);
		panelText += $"\n\n<b>Current State: <color=#{colorHex}>{currentState}</color></b>";

		// Add frog stats and environment information
		panelText += $"\n\nHealth: {_frog.Health}  |  Flies Collected: {_frog.FliesCollected}/{Frog.MaxFlies}";

		// Display current terrain information and speed modifier.
		TerrainType terrain = _frog.GetCurrentTerrain();
		float speedMod = _frog.GetTerrainModifiedSpeed() / _frog.MaxSpeed;
		string terrainColor = "#FFFFFF";
		
		switch (terrain)
		{
			case TerrainType.Normal:
				terrainColor = "#FFFFFF";
				break;
			case TerrainType.Water:
				terrainColor = "#00FFFF";
				break;
			case TerrainType.Sand:
				terrainColor = "#FFFF00";
				break;
			case TerrainType.Mud:
				terrainColor = "#CC6600";
				break;
		}
		
		panelText += $"\nTerrain: <color={terrainColor}>{terrain}</color> | Speed: {(speedMod * 100).ToString("F0")}%";

		// Add information about targets and threats
		if (frogBT.BestFly != null)
		{
			float distToFly = Vector2.Distance(_frog.transform.position, frogBT.BestFly.transform.position);
			panelText += $"\n\n<color=#88FF88>Target Fly:</color> {distToFly.ToString("F1")} units away";
			
			// Show fly score if available
			if (frogBT.FlyScores.ContainsKey(frogBT.BestFly))
			{
				float flyScore = frogBT.FlyScores[frogBT.BestFly];
				panelText += $" (Score: {flyScore.ToString("F2")})";
			}
			
			// Show if best fly is in grid or out of grid
			bool bestFlyInGrid = false;
			if (frogBT.BehaviorBlackboard.TryGetValueForType<Fly, bool>(frogBT.BestFly.GetInstanceID().ToString() + "_inGrid", out bestFlyInGrid))
			{
				string gridStatus = bestFlyInGrid ? "in grid" : "outside grid";
				string colorCode = bestFlyInGrid ? "#88FF88" : "#FFBB88";
				panelText += $" <color={colorCode}>({gridStatus})</color>";
			}
		}
		else
		{
			panelText += "\n<color=#88FF88>No target fly selected by BT</color>";
		}

		// Display information about nearby snakes detected by the BT.
		int snakeCount = frogBT.NearbySnakes.Count; // Use BT's list of nearby snakes.
		if (snakeCount > 0)
		{
			if (frogBT.ClosestSnake != null) // Use BT's closest snake reference.
			{
				float distToSnake = Vector2.Distance(_frog.transform.position, frogBT.ClosestSnake.transform.position);
				string snakeText = $"\n<color=#FF8888>Nearest Snake:</color> {distToSnake.ToString("F1")} units away";
				
				if (distToSnake < frogBT.ScaredRange)
				{
					snakeText += " <b>(DANGER!)</b>";
				}
				
				panelText += snakeText;
			}
			
			if (snakeCount > 1)
			{
				panelText += $"\n<color=#FF8888>Multiple snakes nearby ({snakeCount})!</color>";
			}
		}
		else
		{
			panelText += "\n<color=#88FF88>No snakes nearby (BT)</color>";
		}

		// Display information if the frog is outside screen bounds, including timer from BT.
		bool offScreen = _frog.isOutOfScreen(_frog.transform);
		if (offScreen)
		{
			// Show remaining time if the BT's off-screen timer is active.
			if (frogBT.WasOutsideScreen)
			{
				float timeRemaining = frogBT.MaxTimeOutsideScreen - frogBT.OutsideScreenTimer;
				panelText += $"\n\n<color=#FFCC00>Outside screen: {timeRemaining.ToString("F1")}s remaining</color>";
			}
			else
			{
				panelText += "\n\n<color=#FFCC00>Frog is outside screen bounds!</color>";
			}
		}

		// Display some key Behavior Tree configuration settings.
		panelText += "\n\n<b>BT Settings:</b>";
		panelText += $"\nTarget Switch Threshold: {frogBT.TargetSwitchThreshold.ToString("F2")}";
		if (frogBT.AllowExploreOutsideScreen)
		{
			panelText += $"\nScreen Boundary Tolerance: {(frogBT.ScreenBoundaryTolerance * 100).ToString("F0")}%";
		}

		// Calculate size and position
		Vector2 panelSize = panelStyle.CalcSize(new GUIContent(panelText));
		panelSize.x = 300; // Fixed width for better formatting
		panelSize.y = 220; // Fixed height to accommodate all content

		Rect panelRect = new Rect(
			10, // Left side margin
			(Screen.height - panelSize.y) / 2, // Center vertically
			panelSize.x,
			panelSize.y
		);

		// Draw the Behavior Tree status panel using OnGUI.
		GUI.Box(panelRect, panelText, panelStyle);
	}


	/// <summary>
	/// Draws a panel displaying the current state and relevant data for a Snake's Behavior Tree AI.
	/// It finds the first snake using `Snake.ControlMode.BehaviorTree` and displays its status.
	/// Only drawn via OnGUI if at least one snake uses BT mode and `showSnakeBehaviorTreePanel` is true.
	/// </summary>
	private void DrawSnakeBehaviorTreeState()
	{
		// Check conditions: need snakes, panel must be enabled.
		if (_snakes == null || _snakes.Length == 0 || !showSnakeBehaviorTreePanel) return;

		// Find the first active snake using Behavior Tree control mode.
		SnakeBehaviorTree snakeBT = null;
		Snake activeSnake = null;
		foreach (Snake snake in _snakes)
		{
			if (snake != null && snake.controlMode == Snake.ControlMode.BehaviorTree)
			{
				activeSnake = snake;
				snakeBT = snake.GetComponent<SnakeBehaviorTree>();
				if (snakeBT != null) break;
			}
		}

		// If no snake using BT was found, or it lacks the BT component, exit.
		if (snakeBT == null || activeSnake == null) return;

		// Define the style for the snake behavior tree panel.
		GUIStyle panelStyle = new GUIStyle(GUI.skin.box);
		panelStyle.fontSize = 12;
		panelStyle.normal.textColor = Color.white;
		panelStyle.padding = new RectOffset(15, 15, 15, 15);
		panelStyle.wordWrap = true;

		// Create the panel content
		string panelText = "<b><color=#FFAA88>Snake Behavior Tree Status</color></b>";

		// Add current state - get from behavior tree
		string currentState = snakeBT.CurrentBehaviorName;
		Color stateColor = Color.white;
		// Assign colors based on state name (matches original logic).
		switch (currentState)
		{
			case "Patrol Away":
				stateColor = new Color(0.3f, 0.3f, 0.3f); // Gray
				break;
			case "Patrol Home":
				stateColor = new Color(1.0f, 1.0f, 1.0f); // White
				break;
			case "Attack Frog":
				stateColor = new Color(1.0f, 0.2f, 0.2f); // Red
				break;
			case "Benign":
				stateColor = new Color(0.2f, 0.94f, 0.23f); // Green
				break;
			case "Fleeing":
				stateColor = new Color(0.45f, 0.98f, 0.94f); // Cyan
				break;
		}
		
		string colorHex = ColorUtility.ToHtmlStringRGB(stateColor);
		panelText += $"\n\n<b>Current State: <color=#{colorHex}>{currentState}</color></b>";

		// Add snake basic information
		panelText += $"\n\nSnake: {activeSnake.name}";
		
		// Add distance to frog if it exists
		if (activeSnake.Frog != null)
		{
			float distToFrog = Vector2.Distance(activeSnake.transform.position, activeSnake.Frog.transform.position);
			string frogText = $"\n\n<color=#FF8888>Distance to Frog:</color> {distToFrog.ToString("F1")} units";
			
			// Check if frog is in aggro range
			if (distToFrog <= activeSnake.AggroRange)
			{
				frogText += " <b>(In Aggro Range)</b>";
			}
			else if (distToFrog <= activeSnake.DeAggroRange)
			{
				frogText += " (In DeAggro Range)";
			}
			
			panelText += frogText;
		}
		
		// Add terrain info
		TerrainType snakeTerrain = TerrainType.Normal;
		if (Pathfinding.grid != null)
		{
			snakeTerrain = Pathfinding.grid.GetTerrainTypeAt(activeSnake.transform.position);
		}
		
		// Get terrain color
		string terrainColor = "#FFFFFF";
		switch (snakeTerrain)
		{
			case TerrainType.Normal:
				terrainColor = "#FFFFFF";
				break;
			case TerrainType.Water:
				terrainColor = "#00FFFF";
				break;
			case TerrainType.Sand:
				terrainColor = "#FFFF00";
				break;
			case TerrainType.Mud:
				terrainColor = "#CC6600";
				break;
		}
		
		// Get speed modifier based on terrain
		float speedModifier = 1.0f;
		// Assign colors and modifiers based on terrain type (matches original logic).
		switch (snakeTerrain)
		{
			case TerrainType.Normal:
				speedModifier = 1.0f;
				break;
			case TerrainType.Water:
				speedModifier = 0.8f;
				break;
			case TerrainType.Sand:
				speedModifier = 0.9f;
				break;
			case TerrainType.Mud:
				speedModifier = 0.6f;
				break;
		}
		
		panelText += $"\n\nTerrain: <color={terrainColor}>{snakeTerrain}</color> | Speed: {(speedModifier * 100).ToString("F0")}%";

		// Add movement targets
		if (currentState.Contains("Patrol"))
		{
			if (currentState == "Patrol Away" && activeSnake.PatrolPoint != null)
			{
				float distToTarget = Vector2.Distance(activeSnake.transform.position, activeSnake.PatrolPoint.position);
				panelText += $"\n\n<color=#AAAAFF>Patrol Target:</color> {distToTarget.ToString("F1")} units away";
		}
			else if (currentState == "Patrol Home")
			{
				float distToHome = Vector2.Distance(activeSnake.transform.position, snakeBT.Home);
				panelText += $"\n\n<color=#AAAAFF>Home:</color> {distToHome.ToString("F1")} units away";
			}
		}
		else if (currentState == "Fleeing")
		{
			// Show flee target if available
			float distToFleeTarget = Vector2.Distance(activeSnake.transform.position, snakeBT.FleeTarget);
			panelText += $"\n\n<color=#AAFFFF>Flee Target:</color> {distToFleeTarget.ToString("F1")} units away";
		}

		// Display some key Behavior Tree configuration settings for the snake.
		panelText += "\n\n<b>BT Settings:</b>";
		panelText += $"\nBlend Time: {snakeBT.BehaviorBlendTime.ToString("F2")}s";
		panelText += $"\nPatrol Radius: {snakeBT.PatrolRadius.ToString("F1")} units";
		panelText += $"\nArrive Radius: {snakeBT.ArriveRadius.ToString("F1")} units";

		// Add chase mode status
		panelText += $"\nChase Mode: {(activeSnake.ChaseMode ? "ON" : "OFF")}";

		// Calculate size and position (right side of screen)
		Vector2 panelSize = panelStyle.CalcSize(new GUIContent(panelText));
		panelSize.x = 300; // Fixed width for better formatting
		panelSize.y = 220; // Fixed height to accommodate all content

		Rect panelRect = new Rect(
			Screen.width - panelSize.x - 10, // Right side margin
			(Screen.height - panelSize.y) / 2, // Center vertically
			panelSize.x,
			panelSize.y
		);

		// Draw the Snake Behavior Tree status panel using OnGUI.
		GUI.Box(panelRect, panelText, panelStyle);
	}


	/// <summary>
	/// Unity's legacy Immediate Mode GUI (IMGUI) rendering callback.
	/// Called multiple times per frame for rendering and handling GUI events.
	/// Draws all the custom UI elements like health, flies, and info panels.
	/// </summary>
	void OnGUI()
	{
		// If the game is over, don't draw the regular gameplay UI.
		// The Canvas-based GameOverPanel should be visible instead.
		if (_isGameOver)
		{
			return;
		}

		// --- Draw Core Gameplay UI (Top-Left and Top-Right) ---
		if (showHealthUI) // Only draw if this UI section is enabled.
		{
			// Define position for the first heart icon (top-left).
			int heartX = 10;
			int heartY = 10;

			// Draw heart icons based on the frog's current health.
			if (_frog != null && _heartTex != null) // Check references.
			{
				for (int i = 0; i < _frog.Health; i++)
				{
					// Calculate position for each subsequent heart.
					Rect heartRect = new Rect(heartX + i * (_iconSize + _iconSeparation), heartY, _iconSize, _iconSize);
					GUI.DrawTexture(heartRect, _heartTex); // Draw the heart texture.
				}
			}

			// Define position for the fly counter elements (top-right).
			int flyX = Screen.width - 30;  // Base X position from the right edge.
			int flyY = 10;                 // Y position matching hearts.

			// Draw the fly icon and the collected count text.
			if (_frog != null && _flyTex != null) // Check references.
			{
				// Draw the fly icon to the left of the count text.
				Rect flyIconRect = new Rect(flyX - _iconSize, flyY, _iconSize, _iconSize);
				GUI.DrawTexture(flyIconRect, _flyTex);

				// Define style for the fly count text label.
				GUIStyle countStyle = new GUIStyle(GUI.skin.label);
				countStyle.alignment = TextAnchor.MiddleRight; // Align text to the right.
				countStyle.normal.textColor = Color.white;
				countStyle.fontSize = 16;

				Rect countRect = new Rect(flyX - 100, flyY, 65, _iconSize);
				GUI.Label(countRect, $"{_frog.FliesCollected}/{Frog.MaxFlies}", countStyle);
			}
		}

		// Draw decision tree visualization when in AI mode
		if (_frog != null && _frog.controlMode == Frog.ControlMode.AI)
		{
			DrawFrogDecisionTreeState();
		}
		
		// Draw behavior tree visualization when in BehaviorTree mode
		if (_frog != null && _frog.controlMode == Frog.ControlMode.BehaviorTree)
		{
			DrawFrogBehaviorTreeState();
        }
		// Draw Snake BT panel if applicable (checks internally if any snake uses BT and panel is enabled).
		DrawSnakeBehaviorTreeState();

		// --- Draw Heuristic Visualization Aids (Top-Center and Middle-Left) ---
		// Draw the color legend if heuristic colors are enabled.
		if (showHeuristicColors) DrawHeuristicLegend();
		// Draw the detailed explanation if comparison mode is active.
		if (showHeuristicComparison) DrawHeuristicExplanationPanel();


		// --- Draw Controls and Status Info Panels (Bottom-Left, Bottom-Right, Top-Right) ---
		// Draw the main Controls help panel (bottom-left).
		if (_frog != null && showControlsPanel) // Requires frog reference and panel enabled.
		{
			// Define style for the controls panel box.
			GUIStyle controlModeStyle = new GUIStyle(GUI.skin.box);
			controlModeStyle.normal.textColor = Color.white;
			controlModeStyle.fontSize = 14;
			controlModeStyle.padding = new RectOffset(8, 8, 5, 5);
			
			string controlModeText = GetControlModeText(_frog.controlMode);
			string movementTypeText = _frog.GetMovementTypeString();

			// Combine control mode and movement type
			string modeText = $"Control: {controlModeText}\nMovement: {movementTypeText}";
			
			// Add key information for various modes
			modeText += "\n\nKeys: [A]Control Mode  [T]Movement Type  [M]Snake Movement  [H]Heuristic";
			modeText += "\n[G]Frog BT Panel  [L]Snake BT Panel  [B]Snake Control Mode  [C]Snake Chase Mode";

			// Add UI visibility toggle info
			modeText += "\nUI Toggles: [F1]Decision Tree [F2]Health [F3]Controls [F4]A* Settings [F5]Snake Info [F6]A* Advanced [F7]Toggle Panel [F12]All";
			
			// Add heuristic colors info if active
			if (showHeuristicColors && _pathfinding != null)
			{
				modeText += $"\nHeuristic: {GetCurrentHeuristicColoredName()} [K]Toggle Colors";

				// Add comparison mode info
				if (showHeuristicComparison)
				{
					modeText += " [J]Exit Comparison";
				}
				else
				{
					modeText += " [J]Compare All";
				}
			}
			else
			{
				modeText += "\n[K]Show Heuristic Colors [J]Compare All Heuristics";
			}
			
			Vector2 modeSize = controlModeStyle.CalcSize(new GUIContent(modeText));
			Rect modeRect = new Rect(10, Screen.height - modeSize.y - 10, modeSize.x, modeSize.y);

			// Draw the controls panel box.
			GUI.Box(modeRect, modeText, controlModeStyle);

			// Draw the basic A* settings panel (bottom-right) if relevant conditions met.
			if (_frog.CurrentMovementType == Frog.MovementType.AStar && _pathfinding != null && showAStarPanel)
			{
				// Define style for the A* settings box.
				GUIStyle pathStyle = new GUIStyle(GUI.skin.box);
				pathStyle.normal.textColor = new Color(0.9f, 0.9f, 0.6f); // Yellowish text.
				pathStyle.fontSize = 14;
				pathStyle.padding = new RectOffset(8, 8, 5, 5);

				string pathSettingsText = "A* Settings:";
				pathSettingsText += $"\n• Dynamic Recalc: {(_pathfinding.enableDynamicPathRecalculation ? "ON" : "OFF")} [P]";
				pathSettingsText += $"\n• Periodic Updates: {(_frog.enablePeriodicPathUpdates ? "ON" : "OFF")} [V]";
				pathSettingsText += $"\n• Unsticking: {(_frog.enableStuckDetection ? "ON" : "OFF")} [U]";

				// Calculate size and position panel at the bottom-right.
				Vector2 pathSize = pathStyle.CalcSize(new GUIContent(pathSettingsText));
				Rect pathRect = new Rect(Screen.width - pathSize.x - 10, Screen.height - pathSize.y - 10, pathSize.x, pathSize.y);

				// Draw the A* settings panel box.
				GUI.Box(pathRect, pathSettingsText, pathStyle);
			}
		}

		// Display snake movement type and chase mode
		if (_snakes != null && _snakes.Length > 0 && showSnakePanel)
		{
			// Box for snake controls indicator - increased height to fit all controls
			GUI.Box(new Rect(Screen.width - 220, 130, 210, 140), "");
			
			// Use the first snake's movement type, chase mode, and control mode as a representative
			string snakeMovementType = _snakes[0].GetMovementTypeString();
			string snakeChaseMode = _snakes[0].GetChaseModeString();
			string snakeControlMode = "FSM"; // Default value

			// Try to get control mode (if method exists)
			try
			{
				snakeControlMode = _snakes[0].GetControlModeString();
			}
			catch (System.Exception)
			{
				// Method might not exist if script hasn't been updated
				snakeControlMode = "FSM";
			}

			// Display snake movement type and toggle instructions
			GUI.Label(new Rect(Screen.width - 210, 135, 200, 20), "Snake Movement: " + snakeMovementType);
			GUI.Label(new Rect(Screen.width - 210, 155, 200, 20), "Press M to Toggle All Snakes");

			// Display snake chase mode and toggle instructions
			GUI.Label(new Rect(Screen.width - 210, 175, 200, 20), "Snake Chase Mode: " + snakeChaseMode);
			GUI.Label(new Rect(Screen.width - 210, 195, 200, 20), "Press C to Toggle All Snakes");

			// Display snake control mode and toggle instructions
			GUI.Label(new Rect(Screen.width - 210, 215, 200, 20), "Snake Control: " + snakeControlMode);
			GUI.Label(new Rect(Screen.width - 210, 235, 200, 20), "Press B to Toggle All Snakes");
		}

		// Add A* Controls Box
		if (_pathfinding != null && _frog != null && showAStarAdvancedPanel)
		{
			// Get AStar grid reference
			AStarGrid astarGrid = Pathfinding.grid;
			if (astarGrid == null) return;
			
			// Box for A* controls - increased height to accommodate more toggles
			GUI.Box(new Rect(Screen.width - 220, 280, 210, 290), "A* Advanced Controls");
			
			int yOffset = 300;
			int yIncrement = 22;
			
			// Display and toggle dynamic path recalculation
			bool dynamicPath = _pathfinding.enableDynamicPathRecalculation;
			bool newDynamicPath = GUI.Toggle(new Rect(Screen.width - 210, yOffset, 200, 20), 
				dynamicPath, "Dynamic Path Recalc (P)");
			
			// If value changed, toggle the setting
			if (newDynamicPath != dynamicPath)
			{
				_pathfinding.ToggleDynamicPathRecalculation(newDynamicPath);
			}
			yOffset += yIncrement;
			
			// Display and toggle periodic path updates
			bool periodicUpdates = _frog.enablePeriodicPathUpdates;
			bool newPeriodicUpdates = GUI.Toggle(new Rect(Screen.width - 210, yOffset, 200, 20), 
				periodicUpdates, "Periodic Updates (V)");
			
			// If value changed, toggle the setting
			if (newPeriodicUpdates != periodicUpdates)
			{
				_frog.TogglePeriodicPathUpdates(newPeriodicUpdates);
			}
			yOffset += yIncrement;
			
			// Display and toggle frog unsticking
			bool unsticking = _frog.enableStuckDetection;
			bool newUnsticking = GUI.Toggle(new Rect(Screen.width - 210, yOffset, 200, 20), 
				unsticking, "Frog Unsticking (U)");
			
			// If value changed, toggle the setting
			if (newUnsticking != unsticking)
			{
				_frog.ToggleStuckDetection(newUnsticking);
			}
			yOffset += yIncrement;

			// --- New toggles from original code ---
			bool diagonalMovement = astarGrid.includeDiagonalNeighbours;
			bool newDiagonalMovement = GUI.Toggle(new Rect(Screen.width - 210, yOffset, 200, 20),
				diagonalMovement, "Diagonal Movement (D)");
				
			if (newDiagonalMovement != diagonalMovement)
			{
				astarGrid.ToggleDiagonalMovement(newDiagonalMovement);
			}
			yOffset += yIncrement;
			
			// Display and toggle path smoothing
			bool pathSmoothing = _pathfinding.enablePathSmoothing;
			bool newPathSmoothing = GUI.Toggle(new Rect(Screen.width - 210, yOffset, 200, 20),
				pathSmoothing, "Path Smoothing (S)");
				
			if (newPathSmoothing != pathSmoothing)
			{
				_pathfinding.TogglePathSmoothing(newPathSmoothing);
			}
			yOffset += yIncrement;
			
			// Display and toggle path optimization
			bool pathOptimization = _pathfinding.enablePathOptimization;
			bool newPathOptimization = GUI.Toggle(new Rect(Screen.width - 210, yOffset, 200, 20),
				pathOptimization, "Path Optimization (O)");
				
			if (newPathOptimization != pathOptimization)
			{
				_pathfinding.TogglePathOptimization(newPathOptimization);
			}
			yOffset += yIncrement;
			
			// Display and toggle dynamic obstacles
			bool dynamicObstacles = astarGrid.enableDynamicObstacles;
			bool newDynamicObstacles = GUI.Toggle(new Rect(Screen.width - 210, yOffset, 200, 20),
				dynamicObstacles, "Dynamic Obstacles (Y)");
				
			if (newDynamicObstacles != dynamicObstacles)
			{
				astarGrid.ToggleDynamicObstacles(newDynamicObstacles);
			}
			yOffset += yIncrement;
			
			// Display and toggle weighted nodes
			bool weightedNodes = astarGrid.enableWeightedNodes;
			bool newWeightedNodes = GUI.Toggle(new Rect(Screen.width - 210, yOffset, 200, 20),
				weightedNodes, "Weighted Nodes (W)");
				
			if (newWeightedNodes != weightedNodes)
			{
				astarGrid.ToggleWeightedNodes(newWeightedNodes);
			}
			yOffset += yIncrement;
			
			// Display and toggle periodic grid updates
			bool periodicGridUpdate = astarGrid.enablePeriodicGridUpdate;
			bool newPeriodicGridUpdate = GUI.Toggle(new Rect(Screen.width - 210, yOffset, 200, 20),
				periodicGridUpdate, "Periodic Grid Update (G)");
				
			if (newPeriodicGridUpdate != periodicGridUpdate)
			{
				astarGrid.TogglePeriodicGridUpdate(newPeriodicGridUpdate);
			}
			yOffset += yIncrement;
			
			// Display and toggle corner avoidance
			bool cornerAvoidance = _pathfinding.enableCornerAvoidance;
			bool newCornerAvoidance = GUI.Toggle(new Rect(Screen.width - 210, yOffset, 200, 20),
				cornerAvoidance, "Corner Avoidance (N)");
				
			if (newCornerAvoidance != cornerAvoidance)
			{
				_pathfinding.ToggleCornerAvoidance(newCornerAvoidance);
			}
			yOffset += yIncrement;

			// Display color legend for path visualization
			GUI.Label(new Rect(Screen.width - 210, yOffset, 200, 20), 
				"Path: " + ((!periodicUpdates && !dynamicPath) ? "Raw (Red)" : 
						   (dynamicPath ? "Dynamic (Yellow)" : "Periodic (Green)")));
		}

		// --- Draw Panel Visibility Indicators (Top Bar) ---
		// This draws the bar at the top with ON/OFF buttons for each panel.
		DrawPanelVisibilityIndicators();
	}

	/// <summary>
	/// Draws a bar at the top of the screen showing the visibility status of various UI panels.
	/// Each indicator acts as a button to toggle the corresponding panel's visibility.
	/// Only drawn if `showTogglePanel` is true.
	/// </summary>
	private void DrawPanelVisibilityIndicators()
	{
		// If the toggle panel itself is hidden, don't draw it.
		if (!showTogglePanel) return;

		// Set up panel dimensions
		int indicatorHeight = 24;
		int indicatorWidth = 140;
		int spacing = 5;
		int panelCount = 9; // Total number of panel toggles (increased by 1 for snake behavior tree)

		// Calculate total width required for all buttons and spacing.
		int totalWidth = (indicatorWidth * panelCount) + (spacing * (panelCount - 1));

		// Calculate starting X position to center the bar horizontally.
		int startX = (Screen.width - totalWidth) / 2;
		int startY = 10; // Position at top
		
		// Store current time as toggle time to keep panel visible
		_lastPanelToggleTime = Time.time;

		// Draw background panel
		GUIStyle panelStyle = new GUIStyle(GUI.skin.box);
		panelStyle.normal.background = MakeColorTexture(new Color(0.1f, 0.1f, 0.2f, 0.8f));
		GUI.Box(new Rect(startX - 10, startY - 5, totalWidth + 20, indicatorHeight + 10), "", panelStyle);
		
		// Decision Tree Panel
		DrawPanelIndicator(startX, startY, indicatorWidth, indicatorHeight, 
			"Decision Tree", showDecisionTreePanel, KeyCode.F1);
		
		// Behavior Tree Panel
		DrawPanelIndicator(startX + (indicatorWidth + spacing) * 1, startY, indicatorWidth, indicatorHeight, 
			"Behavior Tree", showBehaviorTreePanel, KeyCode.G);
		
		// Snake Behavior Tree Panel (new)
		DrawPanelIndicator(startX + (indicatorWidth + spacing) * 2, startY, indicatorWidth, indicatorHeight, 
			"Snake BT", showSnakeBehaviorTreePanel, KeyCode.L);
		
		// Health UI
		DrawPanelIndicator(startX + (indicatorWidth + spacing) * 3, startY, indicatorWidth, indicatorHeight, 
			"Health/Flies UI", showHealthUI, KeyCode.F2);
		
		// Controls Panel
		DrawPanelIndicator(startX + (indicatorWidth + spacing) * 4, startY, indicatorWidth, indicatorHeight, 
			"Controls Panel", showControlsPanel, KeyCode.F3);
		
		// A* Settings Panel
		DrawPanelIndicator(startX + (indicatorWidth + spacing) * 5, startY, indicatorWidth, indicatorHeight, 
			"A* Settings", showAStarPanel, KeyCode.F4);
		
		// Snake Info Panel
		DrawPanelIndicator(startX + (indicatorWidth + spacing) * 6, startY, indicatorWidth, indicatorHeight, 
			"Snake Info", showSnakePanel, KeyCode.F5);
		
		// A* Advanced Controls
		DrawPanelIndicator(startX + (indicatorWidth + spacing) * 7, startY, indicatorWidth, indicatorHeight, 
			"A* Advanced", showAStarAdvancedPanel, KeyCode.F6);
		
		// Toggle Panel itself
		DrawPanelIndicator(startX + (indicatorWidth + spacing) * 8, startY, indicatorWidth, indicatorHeight, 
			"Toggle Panel", showTogglePanel, KeyCode.F7);
	}
	
	// Helper method to draw a single panel visibility indicator
	private void DrawPanelIndicator(int x, int y, int width, int height, string name, bool isVisible, KeyCode key)
	{
		// Create indicator style with colored background based on visibility
		GUIStyle indicatorStyle = new GUIStyle(GUI.skin.button);
		indicatorStyle.fontSize = 10;
		indicatorStyle.padding = new RectOffset(5, 5, 2, 2);
		indicatorStyle.alignment = TextAnchor.MiddleCenter;
		indicatorStyle.normal.textColor = Color.white;
		
		// Set colors based on visibility
		if (isVisible)
		{
			// Green for visible panels
			indicatorStyle.normal.background = MakeColorTexture(new Color(0.2f, 0.7f, 0.2f, 0.8f));
			indicatorStyle.hover.background = MakeColorTexture(new Color(0.3f, 0.8f, 0.3f, 0.9f));
			indicatorStyle.active.background = MakeColorTexture(new Color(0.4f, 0.9f, 0.4f, 1.0f));
		}
		else
		{
			// Red for hidden panels
			indicatorStyle.normal.background = MakeColorTexture(new Color(0.7f, 0.2f, 0.2f, 0.8f));
			indicatorStyle.hover.background = MakeColorTexture(new Color(0.8f, 0.3f, 0.3f, 0.9f));
			indicatorStyle.active.background = MakeColorTexture(new Color(0.9f, 0.4f, 0.4f, 1.0f));
		}

		// Draw the indicator button
		string indicatorText = $"{name}: {(isVisible ? "ON" : "OFF")} [{key}]";
		Rect buttonRect = new Rect(x, y, width, height);

		// Draw the button and check if it was clicked in this frame.
		if (GUI.Button(buttonRect, indicatorText, indicatorStyle))
		{
			// If clicked, call the function to toggle the corresponding panel's visibility.
			TogglePanelByKey(key);
		}
	}

	/// <summary>
	/// Toggles the visibility state of a specific UI panel based on the provided KeyCode.
	/// This acts as a central handler for both keyboard input (in Update) and button clicks (in OnGUI).
	/// </summary>
	/// <param name="key">The KeyCode associated with the panel to toggle (e.g., F1, F2, G, L).</param>
	private void TogglePanelByKey(KeyCode key)
	{
		// Use a switch statement to determine which panel's visibility flag to toggle.
		switch (key)
		{
			case KeyCode.F1:
				showDecisionTreePanel = !showDecisionTreePanel;
				_lastPanelToggleTime = Time.time;
				break;
			case KeyCode.F2:
				showHealthUI = !showHealthUI;
				_lastPanelToggleTime = Time.time;
				break;
			case KeyCode.F3:
				showControlsPanel = !showControlsPanel;
				_lastPanelToggleTime = Time.time;
				break;
			case KeyCode.F4:
				showAStarPanel = !showAStarPanel;
				_lastPanelToggleTime = Time.time;
				break;
			case KeyCode.F5:
				showSnakePanel = !showSnakePanel;
				_lastPanelToggleTime = Time.time;
				break;
			case KeyCode.F6:
				showAStarAdvancedPanel = !showAStarAdvancedPanel;
				_lastPanelToggleTime = Time.time;
				break;
			case KeyCode.F7:
				showTogglePanel = !showTogglePanel;
				_lastPanelToggleTime = Time.time;
				break;
			case KeyCode.F12:
				bool allVisible = showDecisionTreePanel && showControlsPanel &&
								 showAStarPanel && showSnakePanel && showAStarAdvancedPanel;
				// Set only those panels to the opposite state.
				showDecisionTreePanel = !allVisible;
				showControlsPanel = !allVisible;
				showAStarPanel = !allVisible;
				showSnakePanel = !allVisible;
				showAStarAdvancedPanel = !allVisible;
				break;
			case KeyCode.G:
				showBehaviorTreePanel = !showBehaviorTreePanel;
				_lastPanelToggleTime = Time.time;
				break;
			case KeyCode.L:
				showSnakeBehaviorTreePanel = !showSnakeBehaviorTreePanel;
				_lastPanelToggleTime = Time.time;
				break;
		}

		// Record the time of the toggle, potentially for UI feedback animations.
		_lastPanelToggleTime = Time.time;
		// Optional: Log which panel was toggled for debugging.
		// Debug.Log($"Toggled panel visibility via key {key}");
	}

	/// <summary>
	/// Helper method to create a simple 1x1 pixel Texture2D of a specified color.
	/// This is commonly used to set solid background colors for IMGUI elements like buttons or boxes.
	/// </summary>
	/// <param name="color">The desired color for the texture.</param>
	/// <returns>A 1x1 Texture2D filled with the specified color.</returns>
	private Texture2D MakeColorTexture(Color color)
	{
		// Create a new 1x1 texture.
		Texture2D texture = new Texture2D(1, 1);
		// Set the single pixel to the desired color.
		texture.SetPixel(0, 0, color);
		// Apply the pixel change to the texture resource.
		texture.Apply();
		return texture;
	}


	/// <summary>
	/// Helper function to convert a Unity Sprite into a Texture2D.
	/// Necessary for using sprite assets with the legacy OnGUI system, which expects Textures.
	/// Handles sprites that might be part of a larger texture atlas by extracting the correct pixel region.
	/// </summary>
	/// <remarks>
	/// Based on code from Unity Answers: http://answers.unity3d.com/questions/651984/convert-sprite-image-to-texture.html
	/// IMPORTANT: For this to work correctly, the source texture asset of the sprite must have "Read/Write Enabled" checked in its import settings.
	/// </remarks>
	/// <param name="sprite">The Sprite asset to convert.</param>
	/// <returns>A Texture2D containing the pixel data of the sprite, or null if the input sprite is null or the texture is not readable.</returns>
	private Texture2D SpriteToTexture(Sprite sprite)
	{
		// Return null immediately if no sprite is provided.
		if (sprite == null) return null;

		if (sprite.rect.width != sprite.texture.width)
		{
			// Create a new Texture2D with the exact dimensions of the sprite rectangle.
			Texture2D texture = new Texture2D((int)sprite.rect.width, (int)sprite.rect.height);
			Color[] pixels = sprite.texture.GetPixels((int)sprite.textureRect.x, (int)sprite.textureRect.y, (int)sprite.textureRect.width, (int)sprite.textureRect.height);
			texture.SetPixels(pixels);
			// Apply the pixel changes to the new texture.
			texture.Apply();

			return texture; // Return the newly created texture containing only the sprite's pixels.
		}
		else
		{
			// If the sprite uses the entire texture, we can potentially return the source texture directly.
			// It still needs to be readable for OnGUI.
			// Returning the reference directly matches the original code's behavior.
			return sprite.texture;
		}
	}
}