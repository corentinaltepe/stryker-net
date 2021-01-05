using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.DataCollection;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.DataCollector.InProcDataCollector;
using Moq;
using Shouldly;
using Stryker.DataCollector;
using Xunit;

namespace Stryker.Core.UnitTest.TestRunners
{
    // mock for the actual MutantControl class injected in the mutated assembly.
    // used for unit test
    public static class MutantControl
    {
        public static bool CaptureCoverage;
        public static int ActiveMutant = -1;
        public static bool ActiveMutantSeen;
        public static bool MustLog;
        public static IList<int>[] GetCoverageData()
        {
            return new List<int>[2];
        }
    }

    public class CoverageCollectorShould
    {
        [Fact]
        public void ProperlyCaptureParams()
        {
            var collector = new CoverageCollector();

            var start = new TestSessionStartArgs
            {
                Configuration = CoverageCollector.GetVsTestSettings(true, null, "Stryker.Core.UnitTest.TestRunners", null)
            };
            var mock = new Mock<IDataCollectionSink>(MockBehavior.Loose);
            collector.Initialize(mock.Object);

            collector.TestSessionStart(start);
            collector.TestCaseStart(new TestCaseStartArgs(new TestCase("theTest", new Uri("xunit://"), "source.cs")));
            MutantControl.CaptureCoverage.ShouldBeTrue();
        }

        [Fact]
        public void SupportMutantSpecificTracing()
        {
            var collector = new CoverageCollector();

            var testCase = new TestCase("theTest", new Uri("xunit://"), "source.cs");
            var otherTestCase = new TestCase("theOtherTest", new Uri("xunit://"), "source.cs");
            var map = new Dictionary<int, IList<string>>{{12, new List<string>(new []{testCase.Id.ToString()})},
                {15, new List<string>(new []{otherTestCase.Id.ToString()})}};
            var start = new TestSessionStartArgs
            {
                Configuration = CoverageCollector.GetVsTestSettings(false, map, "Stryker.Core.UnitTest.TestRunners", new []{12})
            };
            var mock = new Mock<IDataCollectionSink>(MockBehavior.Loose);
            collector.Initialize(mock.Object);

            collector.TestSessionStart(start);
            collector.TestCaseStart(new TestCaseStartArgs(testCase));
            MutantControl.MustLog.ShouldBeTrue();
            MutantControl.ActiveMutant.ShouldBe(12);
            collector.TestCaseEnd(new TestCaseEndArgs(new DataCollectionContext(testCase), TestOutcome.Passed));
            collector.TestCaseStart(new TestCaseStartArgs(otherTestCase));
            MutantControl.MustLog.ShouldBeFalse();
            MutantControl.ActiveMutant.ShouldBe(15);
            collector.TestCaseEnd(new TestCaseEndArgs(new DataCollectionContext(testCase), TestOutcome.Passed));
        }

        [Fact]
        public void ProperlySelectMutant()
        {
            var collector = new CoverageCollector();

            var mutantMap = new Dictionary<int, IList<string>>() {[0] = new List<string>()};

            var start = new TestSessionStartArgs
            {
                Configuration = CoverageCollector.GetVsTestSettings(false, mutantMap, this.GetType().Namespace, null)
            };
            var mock = new Mock<IDataCollectionSink>(MockBehavior.Loose);
            collector.Initialize(mock.Object);

            collector.TestSessionStart(start);

            collector.TestCaseStart(new TestCaseStartArgs(new TestCase("theTest", new Uri("xunit://"), "source.cs")));

            MutantControl.ActiveMutant.ShouldBe(0);
        }
    }
}
