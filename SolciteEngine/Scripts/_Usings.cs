// Shared by every script. The editor's live compiler uses these files too, so scripts need no `using` lines for
// the engine, scripting API, game UI, OpenTK maths, keys or mouse buttons.
global using SolciteEngine.Core;
global using SolciteEngine.Scripting;
global using SolciteEngine.Player.Data; // SaveData, SaveDataException, SaveIgnore
global using SolciteEngine.UI; // TextLabel, Frame, TextButton, Image, ImageButton, BasicObject, CornerProp, GradientProp
global using OpenTK.Mathematics;
global using Keys = OpenTK.Windowing.GraphicsLibraryFramework.Keys;
global using MouseButton = OpenTK.Windowing.GraphicsLibraryFramework.MouseButton;
