using Content.Shared.CF.Input;
using Robust.Client.Input;

namespace Content.Client.CF.Input;

/// <summary>
/// Puts CarpFinder's key functions in the input context a body uses, so their keys work while playing. Stock's
/// contexts are set up in <c>ContentContexts</c>, which is left alone.
/// </summary>
public sealed partial class CFInputSystem : EntitySystem
{
    [Dependency] private IInputManager _input = default!;

    public override void Initialize()
    {
        base.Initialize();
        var human = _input.Contexts.GetContext("human");
        human.AddFunction(CFKeyFunctions.CFTrip);
        human.AddFunction(CFKeyFunctions.CFHighJump);
        human.AddFunction(CFKeyFunctions.CFLongJump);
    }
}
