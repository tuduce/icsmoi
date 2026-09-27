// Generates the profiles under /examples using the real model classes and the
// real ProfileSerializerService, so the JSON is guaranteed to round-trip
// correctly instead of being hand-typed and guessed. Re-run this after any
// change to the node/pin model shapes to regenerate the examples.
//
// Usage: dotnet run --project tools/ExampleProfileGenerator

using icsmoi.Models;
using icsmoi.Services;

var repoRoot = FindRepoRoot(AppContext.BaseDirectory);
var examplesDir = Path.Combine(repoRoot, "examples");
var serializer = new ProfileSerializerService();

await serializer.SaveAsync(BuildAirspeedBuffet(), Path.Combine(examplesDir, "01-airspeed-buffet.icsmoi.json"));
await serializer.SaveAsync(BuildCenteringSpring(), Path.Combine(examplesDir, "02-centering-spring.icsmoi.json"));
await serializer.SaveAsync(BuildButtonTrim(), Path.Combine(examplesDir, "03-button-trim.icsmoi.json"));

Console.WriteLine($"Wrote 3 example profiles to {examplesDir}");
return;

static string FindRepoRoot(string startDir)
{
    var dir = new DirectoryInfo(startDir);
    while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "icsmoi.slnx")))
        dir = dir.Parent;
    return dir?.FullName ?? throw new InvalidOperationException("Could not find repo root (icsmoi.slnx) above " + startDir);
}

static PinViewModel InputPin(NodeViewModel node, string title) =>
    node.Inputs.First(p => p.Title == title);

static PinViewModel OutputPin(NodeViewModel node, string title) =>
    node.Outputs.First(p => p.Title == title);

static void Wire(FfbProfile profile, NodeViewModel sourceNode, PinViewModel sourcePin, NodeViewModel targetNode, PinViewModel targetPin)
{
    profile.Connections.Add(new ConnectionViewModel
    {
        SourceNodeId = sourceNode.Id,
        SourcePinId = sourcePin.Id,
        TargetNodeId = targetNode.Id,
        TargetPinId = targetPin.Id,
    });
}

static FfbProfile BuildAirspeedBuffet()
{
    var profile = new FfbProfile { ProfileName = "Airspeed Buffet (Example)" };

    var airspeed = new SimConnectNodeViewModel { VariableName = "AIRSPEED INDICATED", X = 40, Y = 60 };

    var rangeMap = new RangeMapNodeViewModel { X = 340, Y = 60 };
    InputPin(rangeMap, "InMin").Value = 40;   // knots — below this, no buffet
    InputPin(rangeMap, "InMax").Value = 200;  // knots — buffet reaches full magnitude here
    InputPin(rangeMap, "OutMin").Value = 0;
    InputPin(rangeMap, "OutMax").Value = 0.15; // keep it gentle — see user_manual/08-safety.md

    var force = new ConstantForceOutputNodeViewModel { X = 640, Y = 60 };

    profile.Nodes.Add(airspeed);
    profile.Nodes.Add(rangeMap);
    profile.Nodes.Add(force);

    Wire(profile, airspeed, OutputPin(airspeed, "Value"), rangeMap, InputPin(rangeMap, "Value"));
    Wire(profile, rangeMap, OutputPin(rangeMap, "Result"), force, InputPin(force, "Magnitude"));

    return profile;
}

static FfbProfile BuildCenteringSpring()
{
    var profile = new FfbProfile { ProfileName = "Centering Spring (Example)" };

    var airspeed = new SimConnectNodeViewModel { VariableName = "AIRSPEED INDICATED", X = 40, Y = 60 };

    var stiffnessCurve = new CurveNodeViewModel { Name = "Stiffness Curve", X = 340, Y = 60 };
    stiffnessCurve.Points.Clear();
    stiffnessCurve.Points.Add(new CurvePoint { X = 0, Y = 0.05 });
    stiffnessCurve.Points.Add(new CurvePoint { X = 60, Y = 0.15 });
    stiffnessCurve.Points.Add(new CurvePoint { X = 120, Y = 0.4 });
    stiffnessCurve.Points.Add(new CurvePoint { X = 200, Y = 0.6 });

    var spring = new ConditionOutputNodeViewModel { ConditionKind = ConditionKind.Spring, X = 640, Y = 60 };

    profile.Nodes.Add(airspeed);
    profile.Nodes.Add(stiffnessCurve);
    profile.Nodes.Add(spring);

    Wire(profile, airspeed, OutputPin(airspeed, "Value"), stiffnessCurve, InputPin(stiffnessCurve, "Value"));
    Wire(profile, stiffnessCurve, OutputPin(stiffnessCurve, "Result"), spring, InputPin(spring, "PositiveCoefficient"));
    Wire(profile, stiffnessCurve, OutputPin(stiffnessCurve, "Result"), spring, InputPin(spring, "NegativeCoefficient"));

    return profile;
}

static FfbProfile BuildButtonTrim()
{
    var profile = new FfbProfile { ProfileName = "Button Trim (Example)" };

    var joystick = new JoystickInputNodeViewModel { X = 40, Y = 40 };
    var trimUpPin = (JoystickPinViewModel)joystick.Outputs[0];
    trimUpPin.Assign(JoystickInputKind.Button, 0); // Button 1 = trim up
    var trimDownPin = new JoystickPinViewModel();
    trimDownPin.Assign(JoystickInputKind.Button, 1); // Button 2 = trim down
    joystick.Outputs.Add(trimDownPin);

    var edgeUp = new EdgeDetectorNodeViewModel { Name = "Trim Up Press", X = 340, Y = 20 };
    var edgeDown = new EdgeDetectorNodeViewModel { Name = "Trim Down Press", X = 340, Y = 140 };

    var step = new MathNodeViewModel { Name = "Trim Step", Expression = "([A]-[B])*0.02", X = 640, Y = 80 };

    var integrator = new IntegratorNodeViewModel { Name = "Trim Position", AddPerTick = true, X = 940, Y = 80 };

    var spring = new ConditionOutputNodeViewModel { ConditionKind = ConditionKind.Spring, X = 1240, Y = 80 };
    // A modest baseline centering feel so the trim offset has something to push against.
    InputPin(spring, "PositiveCoefficient").Value = 0.3;
    InputPin(spring, "NegativeCoefficient").Value = 0.3;

    profile.Nodes.Add(joystick);
    profile.Nodes.Add(edgeUp);
    profile.Nodes.Add(edgeDown);
    profile.Nodes.Add(step);
    profile.Nodes.Add(integrator);
    profile.Nodes.Add(spring);

    Wire(profile, joystick, trimUpPin, edgeUp, InputPin(edgeUp, "Input"));
    Wire(profile, joystick, trimDownPin, edgeDown, InputPin(edgeDown, "Input"));
    Wire(profile, edgeUp, OutputPin(edgeUp, "Rising"), step, InputPin(step, "A"));
    Wire(profile, edgeDown, OutputPin(edgeDown, "Rising"), step, InputPin(step, "B"));
    Wire(profile, step, OutputPin(step, "Result"), integrator, InputPin(integrator, "Input"));
    Wire(profile, integrator, OutputPin(integrator, "Result"), spring, InputPin(spring, "Offset"));

    return profile;
}
