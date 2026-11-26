using System.Collections.Generic;
using Content.IntegrationTests.Fixtures;
using Content.Shared.Body;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Markings;
using Content.Shared.Humanoid.Prototypes;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.CF;

/// <summary>
/// The ancestor kobold sprite offers the stock kobold's ears, horns and snouts in the lobby.
/// </summary>
[TestFixture]
public sealed class CFKoboldMarkingsTest : GameTest
{
    private static readonly ProtoId<SpeciesPrototype> Kobold = "CF-Kobold-Animal";
    private static readonly ProtoId<OrganCategoryPrototype> Head = "Head";
    private static readonly ProtoId<OrganCategoryPrototype> Torso = "Torso";

    [Test]
    public async Task TheHeadOffersEarsHornsAndSnouts()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();

        await server.WaitAssertion(() =>
        {
            var markings = server.ResolveDependency<MarkingManager>();
            var data = markings.GetMarkingData(Kobold);

            Assert.That(data, Does.ContainKey(Head));
            var head = data[Head];
            Assert.That(head.Layers, Does.Contain(HumanoidVisualLayers.HeadTop));
            Assert.That(head.Layers, Does.Contain(HumanoidVisualLayers.HeadSide));
            Assert.That(head.Layers, Does.Contain(HumanoidVisualLayers.Snout));

            var top = markings.MarkingsByLayerAndGroupAndSex(HumanoidVisualLayers.HeadTop, head.Group, Sex.Male);
            Assert.That(top.Keys, Does.Contain("CFKoboldHornsKoboldEars"));
            Assert.That(top.Keys, Does.Contain("CFKoboldHornsShort"));

            var side = markings.MarkingsByLayerAndGroupAndSex(HumanoidVisualLayers.HeadSide, head.Group, Sex.Female);
            Assert.That(side.Keys, Does.Contain("CFKoboldHornsFloppyKoboldEars"));

            var snout = markings.MarkingsByLayerAndGroupAndSex(HumanoidVisualLayers.Snout, head.Group, Sex.Male);
            Assert.That(snout.Keys, Does.Contain("CFKoboldSnoutRound"));

            // A kobold with nothing picked gets ears and a snout.
            var picked = new Dictionary<HumanoidVisualLayers, List<Marking>>();
            markings.EnsureValidLimits(picked, head.Group, head.Layers, Color.Green, Color.Black);
            Assert.That(picked[HumanoidVisualLayers.HeadTop][0].MarkingId, Is.EqualTo("CFKoboldHornsKoboldEars"));
            Assert.That(picked[HumanoidVisualLayers.Snout][0].MarkingId, Is.EqualTo("CFKoboldSnoutRound"));
            Assert.That(picked, Does.Not.ContainKey(HumanoidVisualLayers.HeadSide));

            // The body is one drawing with its own tail, so nothing else takes a marking.
            var chest = markings.MarkingsByLayerAndGroupAndSex(HumanoidVisualLayers.Tail, data[Torso].Group, Sex.Male);
            Assert.That(chest, Is.Empty);
        });
    }
}
