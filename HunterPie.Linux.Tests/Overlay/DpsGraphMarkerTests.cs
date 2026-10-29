using HunterPie.Linux.Overlay.ViewModels;
using HunterPie.Linux.Overlay.Views;

namespace HunterPie.Linux.Tests.Overlay;

[TestClass]
public class DpsGraphMarkerTests
{
    [TestMethod]
    public void MarkersAreEveryMultipleOfFiftyBelowTheTop()
    {
        CollectionAssert.AreEqual(new[] { 50.0, 100.0, 150.0, 200.0 }, DpsGraph.MarkerValues(230, 146).ToArray());
    }

    [TestMethod]
    public void AMarkerAtTheTopEdgeIsLeftOut()
    {
        CollectionAssert.AreEqual(new[] { 50.0 }, DpsGraph.MarkerValues(100, 146).ToArray());
    }

    [TestMethod]
    public void NoMarkersWhenTheAxisIsShorterThanTheFirstStep()
    {
        Assert.AreEqual(0, DpsGraph.MarkerValues(40, 146).Count());
        Assert.AreEqual(0, DpsGraph.MarkerValues(0, 146).Count());
        Assert.AreEqual(0, DpsGraph.MarkerValues(500, 0).Count());
    }

    [TestMethod]
    public void AVeryHighAxisThinsTheMarkersOutButKeepsMultiplesOfFifty()
    {
        double[] values = [.. DpsGraph.MarkerValues(3000, 146)];

        Assert.IsTrue(values.Length > 0);
        Assert.IsTrue(values.Length < 59, "all 59 gridlines would be under 3 px apart");
        Assert.IsTrue(values.All(it => it % 50 == 0));
        Assert.IsTrue(values.Zip(values.Skip(1), (a, b) => (b - a) / 3000 * 146).All(it => it >= 10));
    }

    [TestMethod]
    public void MarkersAreOnlyShownWhenTheGraphPlotsDps()
    {
        var view = new DamageViewModel();

        view.GraphMode = GraphMode.DamagePerSecond;
        Assert.IsTrue(view.ShowDpsMarkers);

        view.GraphMode = GraphMode.MovingAverage;
        Assert.IsTrue(view.ShowDpsMarkers);

        view.GraphMode = GraphMode.TotalDamage;
        Assert.IsFalse(view.ShowDpsMarkers);
    }
}
