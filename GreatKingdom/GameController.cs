using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Numerics;
using System.Threading.Tasks;
using Raylib_cs;

namespace GreatKingdom;

public class GameController
{
    private readonly ConfigData _config;
    private const int CellSize = 80;
    private const int GridSize = 9;
    private const int Padding = 40;
    private const int ScreenWidth = (GridSize * CellSize) + (Padding * 2);
    private const int ScreenHeight = ScreenWidth + 80;
    private const int DisplayLimit = 12;

    private const int ButtonWidth = 300;
    private const int ButtonHeight = 60;

    // Menu button Y positions
    private const int MenuButtonHotseat = 200;
    private const int MenuButtonMCTS = 280;
    private const int MenuButtonNeuralNet = 360;
    private const int MenuButtonTrain = 440;
    private const int MenuButtonBrainBattle = 520;
    private const int MenuButtonNetwork = 600;
    private const int MenuButtonLoadBrain = 680;

    // Lobby button Y positions
    private const int LobbyButtonHost = 200;
    private const int LobbyButtonJoin = 300;

    // Training screen button offsets from bottom
    private const int TrainingButtonStopOffset = 80;
    private const int TrainingButtonSaveOffset = 160;
    private const int TrainingButtonLoadOffset = 240;

    // --- Engines & Managers ---
    private readonly Renderer _renderer;
    private readonly MCTS _mcts;
    private DQNAgent _neuralNet;
    private DQNAgent? _neuralNet2; // Second brain for brain-vs-brain training
    private NetworkManager _net;
    private Brain _brainManager;

    // --- Core State ---
    public AppScreen CurrentScreen { get; private set; } = AppScreen.Loading;
    public GameMode CurrentMode { get; private set; } = GameMode.Hotseat;
    public GameState GameState { get; private set; }

    // --- Selection/Threading State ---
    private bool _isBrainReady = false;
    private bool _isTrainingActive = false;
    private Task? _trainingTask = null;

    // --- Brain vs Brain Training State ---
    private bool _isBrainVsBrainActive = false;
    private int _bvbGames = 0;
    private int _brain1Wins = 0;
    private int _brain2Wins = 0;

    // --- Training Visualization State ---
    private GameState? _currentTrainingState = null;
    private readonly object _trainingStateLock = new object();
    private List<int> _flashingPieces = new List<int>();
    private byte _flashingPiecesColor = 0; // Store the color of captured pieces
    private int _flashCount = 0;
    private float _flashTimer = 0f;
    private const float FlashDuration = 0.3f; // Slower, more visible flashing
    private int? _pendingAiMove = null;
    private int? _pendingPlayerMove = null;
    private bool _pendingPass = false;

    // --- UI/Data State ---
    public string LoadStatus { get; private set; } = "Initializing...";
    public bool IsAiThinking { get; private set; } = false;
    public string StatusMessage { get; private set; } = "";
    public string PublicIp { get; private set; } = "Fetching...";
    public string TargetIp { get; private set; } = "";
    
    // --- Training UI State ---
    public string GymMessage { get; private set; } = "";
    public float GymMessageTimer { get; private set; } = 0;
    public string[] AvailableBrains { get; private set; } = Array.Empty<string>();
    public int SelectedBrainIndex { get; private set; } = 0;
    public int ScrollOffset { get; private set; } = 0;

    // --- Private Flags ---
    private float _time = 0.0f;
    private bool _fetchedIp = false;
    private bool _hasAutoSaved = false;
    private const float AutoSaveThreshold = 0.005f;

    // --- Session Stats ---
    private int _gamesPlayedThisSession = 0;
    public int GamesPlayedThisSession => _gamesPlayedThisSession;

    // --- Game End Feedback State ---
    private bool _gameJustEnded = false;
    private float _gameEndTimer = 0f;
    private const float GameEndPauseDuration = 2.0f;
    private Player _previousWinner = Player.None;


    // --- CONSTRUCTOR ---
    public GameController(ConfigData config, Renderer renderer, MCTS mcts, DQNAgent neuralNet, NetworkManager net, Brain brainManager)
    {
        _config = config;
        _renderer = renderer;
        _mcts = mcts;
        
        // Assign non-readonly fields from arguments
        _neuralNet = neuralNet; 
        _net = net;
        _brainManager = brainManager;
        
        GameState = new GameState();
        TargetIp = config.Game.DefaultIP;

        Task.Run(AsyncLoadWorker);
    }
    
    private async void AsyncLoadWorker()
    {
        try
        {
            LoadStatus = "Loading latest brain...";
            await Task.Delay(500); // Give UI time to show message

            string latestPath = Path.Combine("brains", Brain.LatestFileAlias);
            string defaultPath = Path.Combine("brains", "brain_default.bin");

            if (File.Exists(latestPath))
            {
                _neuralNet.LoadModel(latestPath);
                LoadStatus = "Latest brain loaded!";
            }
            else if (File.Exists(defaultPath))
            {
                LoadStatus = "No latest brain, loading default...";
                await Task.Delay(500);
                _neuralNet.LoadModel(defaultPath);
                LoadStatus = "Default brain loaded!";
            }
            else
            {
                LoadStatus = "No brains found! Using random weights.";
                await Task.Delay(1000);
            }
            
            _isBrainReady = true;
        }
        catch (Exception ex)
        {
             Console.WriteLine($"Critical Init Error: {ex.Message}");
             LoadStatus = "FATAL: Brain Load Failed.";
             _isBrainReady = true;
        }
    }

    private void TrainingWorker()
    {
        _neuralNet.SetTrainingMode(true);
        while (_isTrainingActive)
        {
            TrainSingleGame();
        }
    }

    private void TrainSingleGame()
    {
        GameState tState = new GameState();
        int moves = 0;

        var blueExperiences = new List<(GameState state, int action, GameState nextState, bool done, float shapedReward)>();

        while (tState.Winner == Player.None && moves < 80)
        {
            if (!_isTrainingActive) return;

            int action;
            Player current = tState.CurrentTurn;
            GameState oldState = tState.DeepCopy();

            if (current == Player.Blue) action = _neuralNet.GetAction(tState, isTraining: true);
            else action = _mcts.GetBestMove(tState, _config.Game.MCTSIterations);

            tState.ApplyMove(action);

            // Update visualization state first
            lock (_trainingStateLock)
            {
                _currentTrainingState = tState.DeepCopy();
            }

            // Check for captures and trigger flash if game ended
            if (tState.Winner != Player.None)
            {
                var capturedPieces = FindCapturedPieces(oldState, tState);
                if (capturedPieces.Count > 0)
                {
                    byte capturedColor = oldState.Board[capturedPieces[0]];
                    Console.WriteLine($"★★★ CAPTURE! {capturedPieces.Count} pieces at: {string.Join(",", capturedPieces)} ★★★");

                    lock (_trainingStateLock)
                    {
                        _flashingPieces = new List<int>(capturedPieces);
                        _flashingPiecesColor = capturedColor;
                        _flashCount = 0;
                        _flashTimer = 0f;
                    }

                    // Pause 3 seconds to show flashing (10 flashes at 0.3s each)
                    System.Threading.Thread.Sleep(3000);

                    lock (_trainingStateLock)
                    {
                        _flashingPieces.Clear();
                    }
                }
            }

            if (current == Player.Blue) {
                float shapedReward = CalculateShapedReward(oldState, tState);
                blueExperiences.Add((oldState, action, tState, tState.Winner != Player.None, shapedReward));
            }
            moves++;

            // Small delay to make visualization visible
            //System.Threading.Thread.Sleep(100);
        }

        // Assign final rewards after game ends
        float finalReward = (tState.Winner == Player.Blue) ? 1.0f : (tState.Winner == Player.Orange ? -1.0f : 0.0f);

        // Store all experiences with proper rewards (final + shaped)
        foreach (var exp in blueExperiences)
        {
            float totalReward = exp.shapedReward;
            if (exp.done) totalReward += finalReward;
            _neuralNet.Remember(exp.state, exp.action, totalReward, exp.nextState, exp.done);
        }

        // Train after collecting all experiences from the game
        for (int i = 0; i < 5; i++)
        {
            _neuralNet.Train();
        }

        _neuralNet.EndEpisode();
    }

    private List<int> FindCapturedPieces(GameState before, GameState after)
    {
        var captured = new List<int>();
        for (int i = 0; i < 81; i++)
        {
            // A piece was captured if it existed before but not after
            if (before.Board[i] != (byte)Player.None && after.Board[i] == (byte)Player.None)
            {
                captured.Add(i);
            }
        }
        return captured;
    }

    private float CalculateShapedReward(GameState before, GameState after)
    {
        float reward = 0.0f;

        // Count stones for each player
        int blueBefore = 0, orangeBefore = 0;
        int blueAfter = 0, orangeAfter = 0;

        for (int i = 0; i < 81; i++)
        {
            if (before.Board[i] == (byte)Player.Blue) blueBefore++;
            if (before.Board[i] == (byte)Player.Orange) orangeBefore++;
            if (after.Board[i] == (byte)Player.Blue) blueAfter++;
            if (after.Board[i] == (byte)Player.Orange) orangeAfter++;
        }

        // Reward for capturing opponent stones
        int opponentCaptured = orangeBefore - orangeAfter;
        if (opponentCaptured > 0) reward += 0.3f * opponentCaptured;

        // Penalty for losing own stones (getting captured)
        int selfCaptured = blueBefore - blueAfter;
        if (selfCaptured > 0) reward -= 0.5f * selfCaptured;

        return reward;
    }
    
    public void Update(float dt)
    {
        _time += dt;
        if (GymMessageTimer > 0) GymMessageTimer -= dt;

        // Update flash timer
        if (_flashCount < 6) // 6 phases = 3 complete flashes (on/off/on/off/on/off)
        {
            _flashTimer += dt;
            if (_flashTimer >= FlashDuration)
            {
                _flashTimer = 0f;
                _flashCount++;
                if (_flashCount >= 6)
                {
                    lock (_trainingStateLock)
                    {
                        _flashingPieces.Clear();
                    }
                }
            }
        }

        switch (CurrentScreen)
        {
            case AppScreen.Loading:     if (_isBrainReady && _time > 1.0f) CurrentScreen = AppScreen.Menu; break;
            case AppScreen.Menu:        UpdateMenu(); break;
            case AppScreen.NetLobby:    UpdateLobby(); break;
            case AppScreen.Game:        UpdateGame(); break;
            case AppScreen.Training:    UpdateTraining(); break;
            case AppScreen.BrainSelect: UpdateBrainSelect(); break;
            case AppScreen.BrainVsBrain: UpdateBrainVsBrain(); break;
        }
    }
    
    public void Draw(int fps)
    {
        // Get current training state and flashing pieces safely
        GameState? trainingState = null;
        List<int> flashingPieces;
        bool shouldFlash;
        lock (_trainingStateLock)
        {
            trainingState = _currentTrainingState;
            flashingPieces = new List<int>(_flashingPieces);
            shouldFlash = _flashCount % 2 == 0; // Flash on even counts (0,2,4)
        }

        switch (CurrentScreen)
        {
            case AppScreen.Loading:     DrawLoading(); break;
            case AppScreen.Menu:        _renderer.DrawMenu(_isBrainReady, _time, fps, GamesPlayedThisSession); break;
            case AppScreen.NetLobby:    _renderer.DrawLobby(PublicIp, StatusMessage, _time, fps); break;
            case AppScreen.Game:        _renderer.DrawGame(GameState, IsAiThinking, StatusMessage, _time, fps, _flashingPieces, (_flashCount % 2 == 0)); break;
            case AppScreen.Training:    _renderer.DrawTraining(_neuralNet?.GamesPlayed ?? 0, _neuralNet?.CurrentLoss ?? 0, _time, GymMessage, GymMessageTimer, fps, trainingState, flashingPieces, shouldFlash); break;
            case AppScreen.BrainSelect: _renderer.DrawBrainSelect(AvailableBrains, SelectedBrainIndex, ScrollOffset); break;
            case AppScreen.BrainVsBrain: _renderer.DrawBrainVsBrain(_bvbGames, _neuralNet?.CurrentLoss ?? 0, _neuralNet2?.CurrentLoss ?? 0, _brain1Wins, _brain2Wins, _time, GymMessage, GymMessageTimer, fps, trainingState, flashingPieces, shouldFlash); break;
        }
    }

    private void DrawLoading()
    {
        Raylib.ClearBackground(new Color(30, 30, 35, 255));
        int cx = ScreenWidth / 2, cy = ScreenHeight / 2;
        Raylib.DrawPolyLines(new Vector2(cx, cy - 50), 6, 40, _time * 5.0f, Color.Blue);
        Raylib.DrawText("GREAT KINGDOM", cx - 100, cy + 20, 24, Color.White);
        Raylib.DrawText(LoadStatus, cx - 100, cy + 60, 20, Color.Gray);
    }
    
    private bool BtnClick(float y) {
        float w = ButtonWidth, h = ButtonHeight;
        float screenWidth = Raylib.GetScreenWidth();
        float x = screenWidth/2 - w/2;
        Rectangle rec = new Rectangle(x,y,w,h);
        Vector2 mousePos = Raylib.GetMousePosition();
        bool isOver = Raylib.CheckCollisionPointRec(mousePos, rec);

        if (isOver && Raylib.IsMouseButtonPressed(MouseButton.Left)) {
            Console.WriteLine($"Button clicked at Y={y}, Mouse at ({mousePos.X}, {mousePos.Y})");
        }

        if (isOver) {
            if (Raylib.IsMouseButtonReleased(MouseButton.Left)) {
                Console.WriteLine($"Button ACTIVATED at Y={y}");
                return true;
            }
        }
        return false;
    }

    // Find pieces with no liberties (captured pieces)
    private List<int> FindCapturedPieces(GameState state, byte targetColor)
    {
        var capturedPieces = new List<int>();
        bool[] visited = new bool[81];

        for (int i = 0; i < 81; i++)
        {
            if (state.Board[i] == targetColor && !visited[i])
            {
                var group = new List<int>();
                bool hasLiberty = CheckGroupLiberties(state, i, targetColor, visited, group);

                if (!hasLiberty)
                {
                    capturedPieces.AddRange(group);
                }
            }
        }

        return capturedPieces;
    }

    // Check if a group has liberties and collect all pieces in the group
    private bool CheckGroupLiberties(GameState state, int startIdx, byte color, bool[] globalVisited, List<int> group)
    {
        var stack = new Stack<int>();
        stack.Push(startIdx);
        bool foundLiberty = false;

        while (stack.Count > 0)
        {
            int curr = stack.Pop();
            if (group.Contains(curr)) continue;

            group.Add(curr);
            globalVisited[curr] = true;

            int cx = curr % 9;
            int cy = curr / 9;
            int[] dx = { 0, 0, 1, -1 };
            int[] dy = { 1, -1, 0, 0 };

            for (int k = 0; k < 4; k++)
            {
                int nx = cx + dx[k];
                int ny = cy + dy[k];
                if (nx < 0 || nx >= 9 || ny < 0 || ny >= 9) continue;

                int nIdx = ny * 9 + nx;
                byte neighbor = state.Board[nIdx];

                if (neighbor == (byte)Player.None) foundLiberty = true;
                else if (neighbor == color && !group.Contains(nIdx)) stack.Push(nIdx);
            }
        }

        return foundLiberty;
    }

    public void UpdateMenu()
    {
        if (BtnClick(MenuButtonHotseat)) 
		StartGame(GameMode.Hotseat);

        if (BtnClick(MenuButtonMCTS)) 
		StartGame(GameMode.VsAI);

        if (_isBrainReady)
        {
            if (BtnClick(MenuButtonNeuralNet)) 
		StartGame(GameMode.VsNeuralNet);
            if (BtnClick(MenuButtonTrain)) 
		CurrentScreen = AppScreen.Training;
            if (BtnClick(MenuButtonBrainBattle)) 
		StartBrainVsBrainTraining();
            if (BtnClick(MenuButtonLoadBrain)) {
                AvailableBrains = _brainManager.ListAvailableBrains();
                SelectedBrainIndex = 0;
                ScrollOffset = 0;
                CurrentScreen = AppScreen.BrainSelect;
            }
        }

        if (BtnClick(MenuButtonNetwork)) 
		CurrentScreen = AppScreen.NetLobby;
    }

    public void UpdateLobby()
    {
        if (Raylib.IsKeyPressed(KeyboardKey.Escape)) CurrentScreen = AppScreen.Menu;
        
        if (!_fetchedIp) {
            _fetchedIp = true;
            Task.Run(async () => {
                try { using var c = new HttpClient(); PublicIp = await c.GetStringAsync("https://api.ipify.org"); }
                catch { PublicIp = "Unknown"; }
            });
        }
        if (BtnClick(LobbyButtonHost)) {
            StatusMessage = "Starting Server...";
            Task.Run(async () => {
                if(await _net.HostGame()) StartGame(GameMode.Network);
                else StatusMessage = "Error: Port 7777 busy!";
            });
        }
        if (BtnClick(LobbyButtonJoin)) {
            StatusMessage = "Connecting...";
            Task.Run(async () => {
                if(await _net.JoinGame(TargetIp)) StartGame(GameMode.Network);
                else StatusMessage = "Connection Failed.";
            });
        }
    }

    public void UpdateBrainSelect()
    {
        if (Raylib.IsKeyPressed(KeyboardKey.Escape))
        {
            CurrentScreen = AppScreen.Training;
            return;
        }

        if (AvailableBrains.Length > 0)
        {
            if (Raylib.IsKeyPressed(KeyboardKey.Down) || Raylib.IsKeyPressed(KeyboardKey.S))
                SelectedBrainIndex = Math.Min(SelectedBrainIndex + 1, AvailableBrains.Length - 1);
            
            if (Raylib.IsKeyPressed(KeyboardKey.Up) || Raylib.IsKeyPressed(KeyboardKey.W))
                SelectedBrainIndex = Math.Max(SelectedBrainIndex - 1, 0);

            if (SelectedBrainIndex < ScrollOffset) 
		ScrollOffset = SelectedBrainIndex;
            if (SelectedBrainIndex >= ScrollOffset + DisplayLimit) 
		ScrollOffset = SelectedBrainIndex - DisplayLimit + 1;
        }


        if (Raylib.IsKeyPressed(KeyboardKey.Enter) && AvailableBrains.Length > 0)
        {
            string selectedFile = AvailableBrains[SelectedBrainIndex];

            if (_brainManager.LoadBrain(selectedFile))
                GymMessage = $"Loaded {selectedFile}!";
            else
                GymMessage = "Load failed.";

            GymMessageTimer = 3.0f;
            CurrentScreen = AppScreen.Training;
        }
    }

    public void UpdateTraining()
    {
        if (!_isTrainingActive && _isBrainReady && _trainingTask == null)
        {
            _isTrainingActive = true;
            _trainingTask = Task.Run(TrainingWorker);
            _hasAutoSaved = false;
        }

        if (Raylib.IsKeyPressed(KeyboardKey.Escape) || BtnClick(Raylib.GetScreenHeight() - TrainingButtonStopOffset)) 
        {
            _isTrainingActive = false;
            _trainingTask = null;
            CurrentScreen = AppScreen.Menu;
        }
        
        // Auto-Save Check
        if (_isTrainingActive && !_hasAutoSaved && (_neuralNet?.CurrentLoss ?? 1.0f) < AutoSaveThreshold)
        {
            if (_neuralNet != null)
            {
                _brainManager.SaveCurrentBrain(_neuralNet);
                GymMessage = $"AUTO-SAVED! Loss hit {AutoSaveThreshold} at {_neuralNet.GamesPlayed} games.";
                GymMessageTimer = 5.0f;
                _hasAutoSaved = true;
            }
        }

        // Manual Save Button
        if (BtnClick(Raylib.GetScreenHeight() - TrainingButtonSaveOffset)) {
            if (_neuralNet != null)
            {
                _brainManager.SaveCurrentBrain(_neuralNet);
                GymMessage = "Brain Saved!";
                GymMessageTimer = 3.0f;
            }
        }

        // Load Specific Button (Switches Screen)
        if (BtnClick(Raylib.GetScreenHeight() - TrainingButtonLoadOffset)) {
            AvailableBrains = _brainManager.ListAvailableBrains(); 
            SelectedBrainIndex = 0;
            ScrollOffset = 0;
            CurrentScreen = AppScreen.BrainSelect;
        }
    }

    private void StartGame(GameMode mode)
    {
        CurrentMode = mode;
        GameState = new GameState();
        CurrentScreen = AppScreen.Game;
        IsAiThinking = false;
        StatusMessage = mode == GameMode.Network
            ? (_net.IsHost ? "You are BLUE" : "You are ORANGE")
            : "Blue Start!";

        // Reset game end state for new game
        _previousWinner = Player.None;
        _gameJustEnded = false;
        _flashingPieces.Clear();
    }

    public void UpdateGame()
    {
        // Detect when game just ended (winner changed from None to a player)
        if (GameState.Winner != Player.None && _previousWinner == Player.None)
        {
            Console.WriteLine($"Game ended! Winner: {GameState.Winner}, ConsecutivePasses: {GameState.ConsecutivePasses}");
            _gameJustEnded = true;
            _gameEndTimer = GameEndPauseDuration;
            _previousWinner = GameState.Winner;

            // Determine what to flash based on how the game ended
            Player loser = (GameState.Winner == Player.Blue) ? Player.Orange : Player.Blue;

            // Check if it was a territory win (both players passed)
            if (GameState.ConsecutivePasses >= 2)
            {
                // Territory win: Flash all losing player's pieces
                _flashingPieces.Clear();
                for (int i = 0; i < 81; i++)
                {
                    if (GameState.Board[i] == (byte)loser)
                    {
                        _flashingPieces.Add(i);
                    }
                }
                _flashingPiecesColor = (byte)loser;
                StatusMessage = $"{GameState.Winner} WINS by TERRITORY!";
            }
            else
            {
                // Capture win: Flash the captured pieces (pieces with no liberties)
                _flashingPieces = FindCapturedPieces(GameState, (byte)loser);
                _flashingPiecesColor = (byte)loser;
                StatusMessage = $"{GameState.Winner} WINS by CAPTURE!";
                Console.WriteLine($"Found {_flashingPieces.Count} captured pieces: {string.Join(", ", _flashingPieces)}");
            }

            _flashTimer = 0f;
            _flashCount = 0;
        }

        // Update game end timer and flashing
        if (_gameJustEnded)
        {
            _gameEndTimer -= Raylib.GetFrameTime();

            // Update flashing animation
            _flashTimer += Raylib.GetFrameTime();
            if (_flashTimer >= FlashDuration)
            {
                _flashTimer = 0f;
                _flashCount++;
            }

            // After 2 seconds, stop the pause and allow normal input
            if (_gameEndTimer <= 0)
            {
                _gameJustEnded = false;
                _flashingPieces.Clear();
                // Don't return - fall through to normal R key handling below
            }
            else
            {
                // Still in pause period, don't process any other input
                return;
            }
        }

        // Normal R key handling (after pause ends)
        if (Raylib.IsKeyPressed(KeyboardKey.R)) {
            if (GameState.Winner != Player.None || CurrentMode != GameMode.Network) {
                if (GameState.Winner != Player.None) {
                    _gamesPlayedThisSession++;
                    _previousWinner = Player.None;
                }
                CurrentScreen = AppScreen.Menu;
                return;
            }
        }

        if (GameState.Winner != Player.None) return;

        // Capture input at the start of the frame (before any early returns)
        bool isMyTurnForInput = true;
        if ((CurrentMode == GameMode.VsAI || CurrentMode == GameMode.VsNeuralNet) && GameState.CurrentTurn == Player.Orange) isMyTurnForInput = false;
        if (CurrentMode == GameMode.Network) {
            if (_net.IsHost && GameState.CurrentTurn != Player.Blue) isMyTurnForInput = false;
            if (!_net.IsHost && GameState.CurrentTurn != Player.Orange) isMyTurnForInput = false;
        }

        if (isMyTurnForInput && !IsAiThinking && GameState.Winner == Player.None)
        {
            if (Raylib.IsKeyPressed(KeyboardKey.Space)) {
                _pendingPass = true;
            }
            if (Raylib.IsMouseButtonReleased(MouseButton.Left)) {
                Vector2 m = Raylib.GetMousePosition();
                int gx = ((int)m.X - Padding) / CellSize;
                int gy = ((int)m.Y - Padding) / CellSize;
                if (gx >= 0 && gx < 9 && gy >= 0 && gy < 9) {
                    int idx = GameState.Idx(gx, gy);
                    if (GameState.Board[idx] == (byte)Player.None) {
                        _pendingPlayerMove = idx;
                    }
                }
            }
        }

        if (CurrentMode == GameMode.Network)
        {
            while (_net.IncomingMoves.TryDequeue(out int m))
            {
                var tempState = GameState;
                tempState.ApplyMove(m);
                GameState = tempState;
            }
        }

        // Check if we have a pending AI move to apply on main thread
        if (_pendingAiMove.HasValue)
        {
            var tempState = GameState;
            tempState.ApplyMove(_pendingAiMove.Value);
            GameState = tempState;
            _pendingAiMove = null;
            IsAiThinking = false;
            return;
        }

        if (GameState.CurrentTurn == Player.Orange && !IsAiThinking)
        {
            if (CurrentMode == GameMode.VsAI) {
                IsAiThinking = true;
                StatusMessage = "MCTS Thinking...";
                var stateCopy = GameState.DeepCopy();
                Task.Run(() => {
                    int move = _mcts.GetBestMove(stateCopy, _config.Game.MCTSIterations);
                    _pendingAiMove = move;
                });
            }
            else if (CurrentMode == GameMode.VsNeuralNet && _isBrainReady) {
                IsAiThinking = true;
                StatusMessage = "Neural Net Thinking...";
                var stateCopy = GameState.DeepCopy();
                Task.Run(() => {
                    _neuralNet.SetTrainingMode(false);
                    int move = _neuralNet.GetAction(stateCopy, isTraining: false);
                    _pendingAiMove = move;
                });
            }
            if (CurrentMode == GameMode.VsAI || CurrentMode == GameMode.VsNeuralNet) return;
        }

        // Process queued player moves
        if (_pendingPass)
        {
            var tempState = GameState;
            tempState.ApplyMove(-1);
            GameState = tempState;
            if (CurrentMode == GameMode.Network) _net.SendMove(-1);
            _pendingPass = false;
        }
        else if (_pendingPlayerMove.HasValue)
        {
            var tempState = GameState;
            tempState.ApplyMove(_pendingPlayerMove.Value);
            GameState = tempState;
            if (CurrentMode == GameMode.Network) _net.SendMove(_pendingPlayerMove.Value);
            _pendingPlayerMove = null;
        }
    }

    // ==========================================
    //      BRAIN VS BRAIN TRAINING METHODS
    // ==========================================

    private void StartBrainVsBrainTraining()
    {
        Console.WriteLine("StartBrainVsBrainTraining called!");
        var (brain1, brain2) = _brainManager.GetTop2Brains();

        Console.WriteLine($"Brain1: {brain1}, Brain2: {brain2}");

        if (brain1 == null || brain2 == null)
        {
            Console.WriteLine("ERROR: Not enough brains!");
            GymMessage = "ERROR: Need at least 2 saved brains to start training!";
            GymMessageTimer = 5.0f;
            return;
        }

        // Load the two best brains
        _neuralNet2 = new DQNAgent(_config);
        string path1 = Path.Combine("brains", brain1);
        string path2 = Path.Combine("brains", brain2);

        if (!_neuralNet.LoadModel(path1) || !_neuralNet2.LoadModel(path2))
        {
            GymMessage = "ERROR: Failed to load brain models!";
            GymMessageTimer = 5.0f;
            return;
        }

        // Reset stats
        _bvbGames = 0;
        _brain1Wins = 0;
        _brain2Wins = 0;

        GymMessage = $"Loaded: {brain1} vs {brain2}";
        GymMessageTimer = 3.0f;

        CurrentScreen = AppScreen.BrainVsBrain;
    }

    public void UpdateBrainVsBrain()
    {
        if (!_isBrainVsBrainActive && _neuralNet2 != null && _trainingTask == null)
        {
            _isBrainVsBrainActive = true;
            _trainingTask = Task.Run(BrainVsBrainTrainingWorker);
        }

        if (Raylib.IsKeyPressed(KeyboardKey.Escape) || BtnClick(Raylib.GetScreenHeight() - TrainingButtonStopOffset))
        {
            _isBrainVsBrainActive = false;
            _trainingTask = null;

            // Save the best brain as latest.bin
            if (_neuralNet2 != null && _bvbGames > 0)
            {
                // Determine winner by wins, or by lowest loss if tied
                DQNAgent bestBrain;
                string winnerName;

                if (_brain1Wins > _brain2Wins)
                {
                    bestBrain = _neuralNet;
                    winnerName = "Brain 1";
                }
                else if (_brain2Wins > _brain1Wins)
                {
                    bestBrain = _neuralNet2;
                    winnerName = "Brain 2";
                }
                else
                {
                    // Tied on wins, use lowest loss
                    if (_neuralNet.CurrentLoss <= _neuralNet2.CurrentLoss)
                    {
                        bestBrain = _neuralNet;
                        winnerName = "Brain 1";
                    }
                    else
                    {
                        bestBrain = _neuralNet2;
                        winnerName = "Brain 2";
                    }
                }

                // Save the winner as latest.bin
                string latestPath = Path.Combine("brains", Brain.LatestFileAlias);
                bestBrain.SaveModel(latestPath);

                // Reload latest.bin into _neuralNet for immediate use
                _neuralNet.LoadModel(latestPath);

                GymMessage = $"{winnerName} wins! Saved as latest brain.";
                GymMessageTimer = 3.0f;
                Console.WriteLine($"Brain vs Brain training complete. {winnerName} saved as latest.bin");
            }

            CurrentScreen = AppScreen.Menu;
        }
    }

    private void BrainVsBrainTrainingWorker()
    {
        _neuralNet.SetTrainingMode(true);
        _neuralNet2?.SetTrainingMode(true);

        while (_isBrainVsBrainActive)
        {
            TrainBrainVsBrainSingleGame();
        }
    }

    private void TrainBrainVsBrainSingleGame()
    {
        if (_neuralNet2 == null) return;

        GameState tState = new GameState();
        int moves = 0;

        var brain1Experiences = new List<(GameState state, int action, GameState nextState, bool done, float shapedReward)>();
        var brain2Experiences = new List<(GameState state, int action, GameState nextState, bool done, float shapedReward)>();

        while (tState.Winner == Player.None && moves < 80)
        {
            if (!_isBrainVsBrainActive) return;

            int action;
            Player current = tState.CurrentTurn;
            GameState oldState = tState.DeepCopy();

            // Brain 1 plays as Blue, Brain 2 plays as Orange
            if (current == Player.Blue)
            {
                action = _neuralNet.GetAction(tState, isTraining: true);
                float shapedReward = CalculateShapedReward(oldState, tState);
                tState.ApplyMove(action);
                brain1Experiences.Add((oldState, action, tState, tState.Winner != Player.None, shapedReward));
            }
            else
            {
                action = _neuralNet2.GetAction(tState, isTraining: true);
                float shapedReward = CalculateShapedReward(oldState, tState);
                tState.ApplyMove(action);
                brain2Experiences.Add((oldState, action, tState, tState.Winner != Player.None, shapedReward));
            }

            // Update visualization state first
            lock (_trainingStateLock)
            {
                _currentTrainingState = tState.DeepCopy();
            }

            // Check for captures and trigger flash if game ended
            if (tState.Winner != Player.None)
            {
                var capturedPieces = FindCapturedPieces(oldState, tState);
                if (capturedPieces.Count > 0)
                {
                    byte capturedColor = oldState.Board[capturedPieces[0]];
                    Console.WriteLine($"★★★ CAPTURE! {capturedPieces.Count} pieces at: {string.Join(",", capturedPieces)} ★★★");

                    lock (_trainingStateLock)
                    {
                        _flashingPieces = new List<int>(capturedPieces);
                        _flashingPiecesColor = capturedColor;
                        _flashCount = 0;
                        _flashTimer = 0f;
                    }

                    // Pause 3 seconds to show flashing (10 flashes at 0.3s each)
                    System.Threading.Thread.Sleep(3000);

                    lock (_trainingStateLock)
                    {
                        _flashingPieces.Clear();
                    }
                }
            }

            moves++;

            // Small delay to make visualization visible
            //System.Threading.Thread.Sleep(100);
        }

        // Track wins
        if (tState.Winner == Player.Blue) _brain1Wins++;
        else if (tState.Winner == Player.Orange) _brain2Wins++;

        // Assign final rewards
        float brain1FinalReward = (tState.Winner == Player.Blue) ? 1.0f : (tState.Winner == Player.Orange ? -1.0f : 0.0f);
        float brain2FinalReward = (tState.Winner == Player.Orange) ? 1.0f : (tState.Winner == Player.Blue ? -1.0f : 0.0f);

        // Store experiences for Brain 1
        foreach (var exp in brain1Experiences)
        {
            float totalReward = exp.shapedReward;
            if (exp.done) totalReward += brain1FinalReward;
            _neuralNet.Remember(exp.state, exp.action, totalReward, exp.nextState, exp.done);
        }

        // Store experiences for Brain 2
        foreach (var exp in brain2Experiences)
        {
            float totalReward = exp.shapedReward;
            if (exp.done) totalReward += brain2FinalReward;
            _neuralNet2.Remember(exp.state, exp.action, totalReward, exp.nextState, exp.done);
        }

        // Train both brains
        for (int i = 0; i < 5; i++)
        {
            _neuralNet.Train();
            _neuralNet2.Train();
        }

        _neuralNet.EndEpisode();
        _neuralNet2.EndEpisode();

        _bvbGames++;
    }
}
