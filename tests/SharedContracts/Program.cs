using HybridVR.Shared;

int checks = 0;
void Check(bool value, string name)
{
    if (!value) throw new InvalidOperationException(name);
    checks++;
}
Check(VirtualKeyboardLayout.Keys.Length == 104, "Full keyboard layout");
foreach (var key in VirtualKeyboardLayout.Keys)
{
    Check(key.X >= 0 && key.Y >= 0 && key.Width > 0 && key.Height > 0, "Positive key geometry");
    Check(key.X + key.Width <= VirtualKeyboardLayout.Width + .001f &&
        key.Y + key.Height <= VirtualKeyboardLayout.Height + .001f, "Key stays in atlas");
}
Check(PanelHitRegion.Classify(.5, .5, 1, 1, 1) == PanelRegion.Content, "Panel center");
Check(PanelHitRegion.Classify(-.01, .5, 1, 1, 1) == PanelRegion.Move, "Outside edge drag margin");
Check(PanelHitRegion.Classify(-.01, -.01, 1, 1, 1) == PanelRegion.Resize, "Outside corner resize margin");
Check(PanelHitRegion.Classify(-1, .5, 1, 1, 1) == PanelRegion.None, "Outside panel");
Check(PanelHitRegion.Classify(double.NaN, .5, 1, 1, 1) == PanelRegion.None, "Invalid pointer");
Check(ToolDepthPolicy.UsesSceneDepth(true, false, false), "World tools use scene depth");
Check(!ToolDepthPolicy.UsesSceneDepth(false, false, false), "Hand tools stay front-layer");
Check(!ToolDepthPolicy.UsesSceneDepth(true, true, false), "Locked tools stay front-layer");
Check(!ToolDepthPolicy.UsesSceneDepth(true, false, true), "Settings stay front-layer");
Check(ToolDepthPolicy.MatricesMatch(0, 0), "Matching depth projection");
Check(!ToolDepthPolicy.MatricesMatch(.01f, 0), "Reject stale projection");
Check(!ToolDepthPolicy.MatricesMatch(float.NaN, 0), "Reject invalid depth projection");
Check(ToolDepthPolicy.CanBindEyeDepth(2,2,true,false,true,true,1,1,24,512,512,512,512), "Matching eye depth");
Check(!ToolDepthPolicy.CanBindEyeDepth(1,2,true,false,true,true,1,1,24,512,512,512,512), "Reject previous-frame depth");
Check(!ToolDepthPolicy.CanBindEyeDepth(2,2,false,false,true,true,1,1,24,512,512,512,512), "Reject opposite eye depth");
Check(LockedPanelProjection.TryFit(1, 1, 1, 0, 0, 0, 0, 1, 1, out var panel), "Viewport projection");
Check(Math.Abs(panel.Width - 2) < 1e-8 && Math.Abs(panel.Y) < 1e-8, "Centered viewport");
Check(!LockedPanelProjection.TryFit(0, 1, 1, 0, 0, 0, 0, 1, 1, out _), "Reject invalid panel distance");
Check(!PanelInteractionPolicy.CanManipulate(false,false,false,false,true,PanelRegion.Move,false,false), "No hand-panel drag");
Check(PanelInteractionPolicy.CanManipulate(false,false,false,false,true,PanelRegion.Resize,false,false), "Hand-panel resize");
Check(PanelInteractionPolicy.CanManipulate(true,false,false,false,true,PanelRegion.Move,false,false), "World-panel drag");
Console.WriteLine($"Passed {checks} shared contract checks (no game or headset required).");
