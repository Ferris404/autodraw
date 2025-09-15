using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Converters;
using Avalonia.Media;
using Avalonia.Threading;
using SharpHook;
using SharpHook.Native;
using SkiaSharp;
using System.Diagnostics.CodeAnalysis;

namespace Autodraw;


// This is solely for Inputs from the DrawStack stuff.
public class InputAction
{
    public enum ActionType
    {
        LeftClick,
        RightClick,
        MoveTo,
        WriteString,
        KeyDown,
        KeyUp
    }

    public ActionType Action { get; set; }
    public Vector2? Position { get; set; }
    
    public int Delay { get; set; }
    
    public int? Speed { get; set; }
    
    public string? Data { get; set; }

    public InputAction(ActionType action, object? data = null)
    {
        Action = action;
        switch (action)
        {
            case ActionType.LeftClick:
            case ActionType.RightClick:
            case ActionType.MoveTo:
                if (data is Vector2 pos)
                {
                    Position = pos;
                }
                break;

            case ActionType.WriteString:
            case ActionType.KeyDown:
            case ActionType.KeyUp:
                Data = data as string;
                break;
        }
    }

    public void PerformAction()
    {
        switch (Action)
        {
            // Consider adding speed and delay support in actions in the future.
            case ActionType.MoveTo:
                if (!Position.HasValue) return;
                Input.MoveTo((short)Position.Value.X, (short)Position.Value.Y);
                break;

            case ActionType.LeftClick:
                if (!Position.HasValue) return;
                Input.MoveTo((short)Position.Value.X, (short)Position.Value.Y);
                Input.SendClick(Input.MouseTypes.MouseLeft);
                break;

            case ActionType.RightClick:
                if (!Position.HasValue) return;
                Input.MoveTo((short)Position.Value.X, (short)Position.Value.Y);
                Input.SendClick(Input.MouseTypes.MouseRight);
                break;

            case ActionType.WriteString:
                Input.SendText(Data);
                break;

            case ActionType.KeyDown:
                if (Enum.TryParse(typeof(KeyCode), Data, true, out var kc1))
                {
                    Input.SendKeyDown((KeyCode)kc1);
                }
                break;

            case ActionType.KeyUp:
                if (Enum.TryParse(typeof(KeyCode), Data, true, out var kc2))
                {
                    Input.SendKeyUp((KeyCode)kc2);
                }
                break;
        }
    }
}


public static class Drawing
{
    
    // Variables

    private static int _interval = 10000;
    private static int _clickDelay = 1000; // Milliseconds, please multiply by 10000
    
    /// <summary>
    ///  0 indicates DFS, 1 indicates Edge-Following
    /// </summary>
    private static bool _isDrawing;
    private static bool _skipRescan;
    private static bool _isPaused;

    public static int Interval { get => _interval; set => _interval = Math.Max(0, value); }
    public static int ClickDelay { get => _clickDelay; set => _clickDelay = Math.Max(0, value); }
    public static byte ChosenAlgorithm { get; set; } = 0;
    public static bool NoRescan { get; set; }
    public static bool IsDrawing { get => _isDrawing; private set => _isDrawing = value; }
    public static bool SkipRescan { get => _skipRescan; private set => _skipRescan = value; }
    public static bool IsPaused { get => _isPaused; private set => _isPaused = value; }
    public static bool FreeDraw2 { get; set; }
    public static Vector2 LastPos { get; set; } = Config.Preview_LastLockPos;
    public static bool ShowPopup { get; set; } = Config.GetEntry("showPopup") == null || bool.Parse(Config.GetEntry("showPopup") ?? "true");


    private static DrawDataDisplay? _dataDisplay;

    // Functions

    public static async Task NOP(long durationTicks)
    {
        var sw = Stopwatch.StartNew();

        while (sw.ElapsedTicks < durationTicks)
            if (durationTicks - sw.ElapsedTicks > 150000)
                await Task.Delay(1);
    }

    private static unsafe byte[,] Scan(SKBitmap bitmap)
    {
        var _pixelArray = new byte[bitmap.Width, bitmap.Height];
        var bitPtr = (byte*)bitmap.GetPixels().ToPointer();

        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var redByte = *bitPtr++;
                bitPtr++;
                bitPtr++;
                bitPtr++;

                _pixelArray[x, y] = redByte < 127 ? (byte)1 : (byte)0;
            }
        }

        return _pixelArray;
    }

    public static void Halt()
    {
        IsDrawing = false;
    }

    [SuppressMessage("Major Code Smell", "S3776:Complexity", Justification = "Algorithmic exploration inherently complex")]
    private static List<Dictionary<Vector2, int>> GetChunks(SKBitmap srcBitmap)
    {
        List<List<byte>> data = new List<List<byte>>();
        for (int x = 0; x < srcBitmap.Width; x++)
        {
            List<byte> column = new List<byte>();
            for (int y = 0; y < srcBitmap.Height; y++)
            {
                var Color = srcBitmap.GetPixel(x, y);
                Color.ToHsv(out _, out _, out float v);
                bool B = v < 50;
                column.Add(B ? (byte)1 : (byte)0);
            }
            data.Add(column);
        }
        
        List<Dictionary<Vector2, int>> chunks = new(); // ah yes, list dictionary tuple-array vector2.
        void Search(int x, int y)
        {
            var stack = new Stack<(int, int)>(); 
            stack.Push((x, y));
            data[x][y] = 2; // Mark as visited

            var chunk = new Dictionary<Vector2, int>();

            // This is practically the same as the AutoDraw code lol.
            while (stack.Count > 0)
            {
                (x, y) = stack.Pop(); 

                // Explore neighbors (sides and corners)

                // Left
                if (x > 0 && data[x - 1][y] == 1) 
                {
                    data[x - 1][y] = 2;
                    stack.Push((x - 1, y));
                    chunk[new Vector2(x - 1, y)] = 1;
                }
                else if(x > 0 && data[x - 1][y] == 0) chunk[new Vector2(x , y)] = 2;

                // Right
                if (x < srcBitmap.Width - 1 && data[x + 1][y] == 1) 
                {
                    data[x + 1][y] = 2;
                    stack.Push((x + 1, y));
                    chunk[new Vector2(x + 1, y)] = 1;
                }
                else if(x < srcBitmap.Width - 1 && data[x + 1][y] == 0) chunk[new Vector2(x , y)] = 2;

                // Up
                if (y > 0 && data[x][y - 1] == 1) 
                {
                    data[x][y - 1] = 2;
                    stack.Push((x, y - 1));
                    chunk[new Vector2(x, y - 1)] = 1;
                }
                else if(y > 0 && data[x][y - 1] == 0) chunk[new Vector2(x , y)] = 2;

                // Down
                if (y < srcBitmap.Height - 1 && data[x][y + 1] == 1) 
                {
                    data[x][y + 1] = 2;
                    stack.Push((x, y + 1));
                    chunk[new Vector2(x, y + 1)] = 1;
                }
                else if(y < srcBitmap.Height - 1 && data[x][y + 1] == 0) chunk[new Vector2(x , y)] = 2;

                // Top-Left
                if (x > 0 && y > 0 && data[x - 1][y - 1] == 1)
                {
                    data[x - 1][y - 1] = 2;
                    stack.Push((x - 1, y - 1));
                    chunk[new Vector2(x - 1, y - 1)] = 1;
                }
                else if (x > 0 && y > 0 && data[x - 1][y - 1] == 0) chunk[new Vector2(x, y)] = 2;

                // Top-Right
                if (x < srcBitmap.Width - 1 && y > 0 && data[x + 1][y - 1] == 1)
                {
                    data[x + 1][y - 1] = 2;
                    stack.Push((x + 1, y - 1));
                    chunk[new Vector2(x + 1, y - 1)] = 1;
                }
                else if (x < srcBitmap.Width - 1 && y > 0 && data[x + 1][y - 1] == 0) chunk[new Vector2(x, y)] = 2;

                // Bottom-Left
                if (x > 0 && y < srcBitmap.Height - 1 && data[x - 1][y + 1] == 1)
                {
                    data[x - 1][y + 1] = 2;
                    stack.Push((x - 1, y + 1));
                    chunk[new Vector2(x - 1, y + 1)] = 1;
                }
                else if (x > 0 && y < srcBitmap.Height - 1 && data[x - 1][y + 1] == 0) chunk[new Vector2(x, y)] = 2;

                // Bottom-Right
                if (x < srcBitmap.Width - 1 && y < srcBitmap.Height - 1 && data[x + 1][y + 1] == 1)
                {
                    data[x + 1][y + 1] = 2;
                    stack.Push((x + 1, y + 1));
                    chunk[new Vector2(x + 1, y + 1)] = 1;
                }
                else if (x < srcBitmap.Width - 1 && y < srcBitmap.Height - 1 && data[x + 1][y + 1] == 0) chunk[new Vector2(x, y)] = 2;
            }
            chunks.Add(chunk);
        }
        for (int y = 0; y < srcBitmap.Height; y++)
            for (int x = 0; x < srcBitmap.Width; x++)
            {
                if (data[x][y] == 1)
                {
                    Search(x,y);
                }
            }
        
        chunks = chunks
            .OrderByDescending(d => d.Count)
            .ToList();
        
        return chunks;
    }

    private static List<List<Vector2>> GenerateActions(List<Dictionary<Vector2, int>> chunks, byte[,] data)
    {
        Vector2[] relativeDirections =
        {
            new(0, -1),    // Up
            new(1, 0),     // Right
            new(0, 1),     // Down
            new(-1, 0),    // Left
            new(-1, -1),   // Top-Left (Diagonal)
            new(1, -1),    // Top-Right (Diagonal)
            new(1, 1),     // Bottom-Right (Diagonal)
            new(-1, 1)     // Bottom-Left (Diagonal)
        };

        List<List<Vector2>> actions = new();

        // Traverse each chunk
        foreach (Dictionary<Vector2, int> chunk in chunks)
        {
            foreach (Vector2 startKey in chunk.Keys)
            {
                if (data[(int)startKey.X, (int)startKey.Y] != 1) continue;

                // Perform DFS to find connected components
                actions.Add(ChosenFunction(startKey, data, relativeDirections));
            }
        }

        return actions;
    }

    private static List<Vector2> ChosenFunction(Vector2 start, byte[,] data, Vector2[] relativeDirections)
    {
        if (ChosenAlgorithm == 0)
        {
            return DFS(start, data, relativeDirections);
        }
        if (ChosenAlgorithm == 1)
        {
            return EdgeTraversal(start, data, relativeDirections);
        }

        return DFS(start, data, relativeDirections); // This really shouldn't happen.
    }
    
    private static List<Vector2> EdgeTraversal(Vector2 start, byte[,] data, Vector2[] directions)
    {
        List<Vector2> path = new();
        Vector2 currentPosition = start;
        int currentDirection = 1;

        while (true)
        {
            bool moved = false;

            foreach (int directionIndex in GetDirectionOrder(currentDirection))
            {
                Vector2 newPosition = currentPosition + directions[directionIndex];
                if (IsValidMove(newPosition, data))
                {
                    path.Add(newPosition);
                    currentPosition = newPosition;
                    currentDirection = directionIndex;
                    data[(int)newPosition.X, (int)newPosition.Y] = 2; // Mark as traveled
                    moved = true;
                    break;
                }
            }

            if (!moved)
                break;
        }
        return path;
    }

    private static List<Vector2> DFS(Vector2 start, byte[,] data, Vector2[] directions)
    {
        Stack<Vector2> stack = new();
        List<Vector2> path = new();

        Vector2? previousPosition = null;

        stack.Push(start);
        data[(int)start.X, (int)start.Y] = 2;

        while (stack.Count > 0)
        {
            Vector2 currentPosition = stack.Pop();

            if (previousPosition.HasValue && !IsAdjacent(previousPosition.Value, currentPosition, directions))
            {
                List<Vector2> aStarPath = AStar(previousPosition.Value, currentPosition, data);
                path.AddRange(aStarPath);

                foreach (var position in aStarPath)
                {
                    data[(int)position.X, (int)position.Y] = 2;
                }
            }

            path.Add(currentPosition);
            previousPosition = currentPosition;

            foreach (var neighbor in directions
                         .Select(direction => currentPosition + direction)
                         .Where(neighbor => IsValidMove(neighbor, data)))
            {
                data[(int)neighbor.X, (int)neighbor.Y] = 2;
                stack.Push(neighbor);
            }
        }

        return path;
    }

    private static bool IsAdjacent(Vector2 position1, Vector2 position2, Vector2[] directions)
    {
        return directions.Any(direction => position1 + direction == position2);
    }

    [SuppressMessage("Major Code Smell", "S3776:Complexity", Justification = "A* pathfinding complexity is expected")]
    private static List<Vector2> AStar(Vector2 start, Vector2 goal, byte[,] data)
    {
        PriorityQueue<Vector2, float> openSet = new();
        HashSet<Vector2> closedSet = new();
        Dictionary<Vector2, Vector2?> cameFrom = new();
        Dictionary<Vector2, float> gScore = new();
        Dictionary<Vector2, float> fScore = new();

        openSet.Enqueue(start, 0);
        gScore[start] = 0;
        fScore[start] = Heuristic(start, goal);

        while (openSet.Count > 0)
        {
            Vector2 current = openSet.Dequeue();

            if (current == goal)
            {
                return ReconstructPath(cameFrom, current);
            }

            closedSet.Add(current);

            for (int i = 0; i < 8; i++)
            {
                Vector2 neighbor = current + GetRelativeDirection(i);
                if (!IsWithinBounds(neighbor, data) || data[(int)neighbor.X, (int)neighbor.Y] == 0 || closedSet.Contains(neighbor))
                {
                    continue;
                }

                float tentativeGScore = gScore[current] + 1;

                if (!gScore.ContainsKey(neighbor) || tentativeGScore < gScore[neighbor])
                {
                    cameFrom[neighbor] = current;
                    gScore[neighbor] = tentativeGScore;
                    fScore[neighbor] = gScore[neighbor] + Heuristic(neighbor, goal);

                    if (!openSet.UnorderedItems.Any(item => item.Element == neighbor))
                    {
                        openSet.Enqueue(neighbor, fScore[neighbor]);
                    }
                }
            }
        }

        return new();
    }

    private static float Heuristic(Vector2 a, Vector2 b)
    {
        return Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);
    }

    private static List<Vector2> ReconstructPath(Dictionary<Vector2, Vector2?> cameFrom, Vector2 current)
    {
        List<Vector2> path = new();
        while (cameFrom.TryGetValue(current, out var prev) && prev.HasValue)
        {
            path.Add(current);
            current = prev.Value;
        }

        path.Reverse();
        return path;
    }
    
    private static IEnumerable<int> GetDirectionOrder(int currentDirection)
    {
        return new[]
        {
            (currentDirection + 3) % 4,  // Left
            currentDirection,            // Forward
            (currentDirection + 1) % 4,  // Right
            (currentDirection + 2) % 4,  // Backward
            4, 5, 6, 7                   // Diagonals
        };
    }

    private static Vector2 GetRelativeDirection(int directionIndex)
    {
        return directionIndex switch
        {
            0 => new Vector2(0, -1),  // Up
            1 => new Vector2(1, 0),   // Right
            2 => new Vector2(0, 1),   // Down
            3 => new Vector2(-1, 0),  // Left
            4 => new Vector2(-1, -1), // Top-Left
            5 => new Vector2(1, -1),  // Top-Right
            6 => new Vector2(1, 1),   // Bottom-Right
            7 => new Vector2(-1, 1),  // Bottom-Left
            _ => throw new ArgumentOutOfRangeException(nameof(directionIndex), "Invalid direction index.")
        };
    }

    private static bool IsWithinBounds(Vector2 position, byte[,] data)
    {
        return position.X >= 0 && position.Y >= 0 &&
               position.X < data.GetLength(0) && position.Y < data.GetLength(1);
    }
    
    

    private static bool IsValidMove(Vector2 position, byte[,] data)
    {
        return position.X >= 0 && position.Y >= 0 &&
               position.X < data.GetLength(0) && position.Y < data.GetLength(1) &&
               data[(int)position.X, (int)position.Y] == 1;
    }

    [SuppressMessage("Major Code Smell", "S3776:Complexity", Justification = "Event + IO orchestration")]
    [SuppressMessage("Minor Code Smell", "S2589:Boolean expressions should not be gratuitous", Justification = "stackHalted is closure-updated by event")]
    public static async Task<bool> DrawStack(List<SKBitmap> stack, List<InputAction> actions, Vector2 position)
    {
        bool stackHalted = false;
        void KeybindRelease(object? sender, KeyboardHookEventArgs e)
        {
            if (e.Data.KeyCode == Config.Keybind_StopDrawing) { stackHalted = true; }
        }
        Input.taskHook.KeyReleased += KeybindRelease;

        foreach (SKBitmap bitmap in stack)
        {
            List<InputAction> actionsCopy = new(actions.Select(act => new InputAction(act.Action, act.Data is not null ? act.Data : act.Position)));
            if (stackHalted)
            {
                break;
            }
            
            // Pre-Process Actions:
            Color color = ImageProcessing.GetColor(bitmap);
            string hex = ColorToHexConverter.ToHexString(color, AlphaComponentPosition.Trailing); // Why yes I AM feeling lazy today! Thanks avalonia for this lol
            hex = hex.Substring(0, 6);
            Console.WriteLine(hex);
            
            foreach (var act in actionsCopy)
            {
                if (act.Action == InputAction.ActionType.WriteString && !string.IsNullOrEmpty(act.Data))
                {
                    // Replace all occurrences of "{colorHex}" in the Data property
                    act.Data = act.Data.Replace("{colorHex}", hex);
                    Console.WriteLine(act.Data);
                    Console.WriteLine(hex);
                }
            }
            
            // Use the Actions :D
            foreach (var act in actionsCopy)
            {
                act.PerformAction();
                await NOP(1000000);
            }
            
            if (stackHalted)
            {
                break;
            }
            
            SKBitmap processedBitmap = ImageProcessing.Process(bitmap, ImageProcessing._currentFilters);
            await NOP(1000000);
            await Draw(processedBitmap,position);
        }

        Input.taskHook.KeyReleased -= KeybindRelease;
        return true;
    }

    [SuppressMessage("Major Code Smell", "S3776:Complexity", Justification = "User interaction + UI updates")]
    public static async Task<bool> Draw(SKBitmap bitmap,Vector2 position)
    {
        if (IsDrawing) return false;

        static void KeybindPress(object? sender, KeyboardHookEventArgs e)
        {
            if (e.Data.KeyCode == Config.Keybind_SkipRescan)
            {
                if (NoRescan) return;
                SkipRescan = true;
            }
        }

        static void KeybindRelease(object? sender, KeyboardHookEventArgs e)
        {
            if (e.Data.KeyCode == Config.Keybind_StopDrawing) Halt();
            if (e.Data.KeyCode == Config.Keybind_SkipRescan)
            {
                if (NoRescan) return;
                SkipRescan = false;
            }

            if (e.Data.KeyCode == Config.Keybind_PauseDrawing) IsPaused = !IsPaused;
        }

        // Capture local for event to update stackHalted (only used in DrawStack). Here we just wire handlers.
        Input.taskHook.KeyPressed += KeybindPress;
        Input.taskHook.KeyReleased += KeybindRelease;

        IsDrawing = true;
        var usedPos = position;

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            _dataDisplay = new DrawDataDisplay();
            _dataDisplay.Show();
            _dataDisplay.Position =
                new PixelPoint((int)(usedPos.X + bitmap.Width), (int)(usedPos.Y + bitmap.Height));
        });

        LastPos = usedPos;
        Pos startPos = new() { X = (int)usedPos.X, Y = (int)usedPos.Y };
        Input.MoveTo((short)startPos.X, (short)startPos.Y);
        await NOP(50000);
        Input.SendClick(Input.MouseTypes.MouseLeft);
        await NOP(50000);

        byte[,] dataArray = Scan(bitmap);

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            _dataDisplay!.DataDisplayText.Text =
                $"Getting Chunks...";
        });
        List<Dictionary<Vector2, int>> Chunks = GetChunks(bitmap);
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            _dataDisplay!.DataDisplayText.Text =
                $"Generating Action Path...";
        });
        List<List<Vector2>> Actions = GenerateActions(Chunks,dataArray);

        int ActionsComplete = 0;
        foreach (List<Vector2> Action in Actions)
        {
            var sw = Stopwatch.StartNew();
            
            ActionsComplete++;
            bool isDown = false;
            int ActionComplete = 0;
            foreach (Vector2 p in Action)
            {
                ActionComplete++;
                if (!IsDrawing) break;
                short x = (short)(p.X + startPos.X);
                short y = (short)(p.Y + startPos.Y);
                await Dispatcher.UIThread.InvokeAsync(() => // Note, this may be slowing down the top-speed, need further testing.
                {
                    _dataDisplay!.DataDisplayText.Text =
                        $"ActionSet Completed: {ActionComplete}/{Action.Count}\n" +
                        $"ActionSet's Remaining: {ActionsComplete}/{Actions.Count}";
                });
                if (!isDown)
                {
                    isDown = true;
                    Vector2 currentPosition = Input.mousePos;
                    Vector2 targetPosition = new Vector2(x, y);
                    int steps = 100;
                    float stepDelay = ClickDelay * 2500f / steps;

                    for (int i = 1; i <= steps; i++)
                    {
                        var interpP = Vector2.Lerp(currentPosition, targetPosition, i / (float)steps);
                        short interpX = (short)interpP.X;
                        short interpY = (short)interpP.Y;

                        Input.MoveTo(interpX, interpY);
                        await NOP((long)stepDelay);
                    }

                    for (int i = 0; i < 10; i++)
                    {
                        Input.MoveTo((short)(x-1), y);
                        await NOP(ClickDelay * 500);
                        Input.MoveTo(x, y);
                    }
                    Input.SendClickDown(Input.MouseTypes.MouseLeft);
                }
                else
                {
                    if (FreeDraw2 && ActionComplete % 10000 == 0)
                    { // Free Draw Mass Draw Protection
                        Utils.Log("Free Draw Click");
                        Input.SendClickUp(Input.MouseTypes.MouseLeft);
                        Input.SendClickDown(Input.MouseTypes.MouseLeft);
                    }
                }
                if (IsPaused)
                {
                    Input.SendClickUp(Input.MouseTypes.MouseLeft);
                    while (IsPaused) await NOP(500000);
                    Input.MoveTo(x, y);
                    await NOP(500000);
                    Input.SendClickDown(Input.MouseTypes.MouseLeft);
                }
                
                Input.MoveTo(x, y);
                await NOP(Interval);
            }
            Input.SendClickUp(Input.MouseTypes.MouseLeft);
            sw.Stop();
            var timeCompMs = sw.Elapsed.TotalMilliseconds;
            
            Utils.Log($"Time per Action: {timeCompMs/Action.Count}");
            Utils.Log($"Action Count: {Action.Count}");
            
            if (FreeDraw2)
            {
                var timeLim = 1000 - timeCompMs;
                if (timeLim > 0)
                {
                    Console.WriteLine(timeLim);
                    await NOP((long)timeLim * 10_000);
                }
            }

            await NOP(ClickDelay * 2500);
            if (!IsDrawing) break;
        }

        Input.taskHook.KeyPressed -= KeybindPress;
        Input.taskHook.KeyReleased -= KeybindRelease;

        IsDrawing = false;
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            _dataDisplay!.Close();
            if (ShowPopup) new MessageBox().ShowMessageBox("Drawing Finished!", "The drawing has finished! Yippee!");
        });
        
        return true;
    }

    private sealed class Pos
    {
        public int X { get; set; }
        public int Y { get; set; }
    }
}