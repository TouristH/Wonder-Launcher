# Wonder headless UI tests

The test application creates an isolated Fluent Dark environment and never reads a user's saved workspace. Tests send Avalonia headless mouse and keyboard input to a real `WorkspaceView`; component and region gestures wait 550 ms to exceed the 450 ms long-press threshold. Divider tests press and drag immediately across both ends of the shared edge.

Run all UI tests from the repository root:

```powershell
dotnet test tests/Wonder.Desktop.Tests/Wonder.Desktop.Tests.csproj -c Release
```

The preview test saves `artifacts/wonder-ui-preview.png` below the test output directory. Set `WONDER_UI_PREVIEW_PATH` to choose a specific output file:

```powershell
$env:WONDER_UI_PREVIEW_PATH = Join-Path $PWD 'artifacts/wonder-ui-preview.png'
dotnet test tests/Wonder.Desktop.Tests/Wonder.Desktop.Tests.csproj -c Release --filter Demo_workspace_renders
```

The renderer uses Skia with headless drawing disabled so the preview contains actual pixels. The PNG supports visual inspection; the suite does not claim pixel-perfect baseline comparison or operating-system window behavior.

`MainWindowPreviewTests` also renders the complete production window using an isolated temporary workspace file. Set `WONDER_MAIN_WINDOW_PREVIEW_PATH` to choose its output; the default is `artifacts/wonder-main-window.png` below the test output directory. The test deletes its temporary workspace after closing the window.

Avalonia Headless 12.1.1 depends on xUnit v3, so this new test project uses xUnit 3.2.2 independently of the older repository tests.

The geometry cases cover signed component offsets from the full region center, window and divider resizing, clipping without position or scale changes, region swaps, and one-time migration of v1 positions. Visual checks compare rendered pixels in polygon lobes and transparent corners to verify hover, pressed, and keyboard focus feedback. Set `WONDER_POLYGON_PREVIEW_PATH` or `WONDER_DIVIDER_PREVIEW_PATH` to override the generated highlight screenshots.
