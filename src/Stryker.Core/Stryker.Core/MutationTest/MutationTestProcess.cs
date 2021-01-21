using System.Collections.Generic;
using System.IO.Abstractions;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Stryker.Core.CoverageAnalysis;
using Stryker.Core.Exceptions;
using Stryker.Core.Logging;
using Stryker.Core.MutantFilters;
using Stryker.Core.Mutants;
using Stryker.Core.Options;
using Stryker.Core.ProjectComponents;
using Stryker.Core.Reporters;

namespace Stryker.Core.MutationTest
{
    public interface IMutationTestProcessProvider
    {
        IMutationTestProcess Provide(MutationTestInput mutationTestInput, IReporter reporter, IMutationTestExecutor mutationTestExecutor, IStrykerOptions options);
    }

    public class MutationTestProcessProvider : IMutationTestProcessProvider
    {
        public IMutationTestProcess Provide(MutationTestInput mutationTestInput,
            IReporter reporter,
            IMutationTestExecutor mutationTestExecutor,
            IStrykerOptions options)
        {
            return new MutationTestProcess(mutationTestInput, reporter, mutationTestExecutor, options: options);
        }
    }

    public interface IMutationTestProcess
    {
        MutationTestInput Input { get; }
        void Mutate();
        StrykerRunResult Test(IEnumerable<Mutant> mutantsToTest);
        void GetCoverage();
        void FilterMutants();
    }

    public class MutationTestProcess : IMutationTestProcess
    {
        public MutationTestInput Input { get; }
        private readonly IProjectComponent _projectContents;
        private readonly ILogger _logger;
        private readonly IMutationTestExecutor _mutationTestExecutor;
        private readonly IReporter _reporter;
        private readonly ICoverageAnalyser _coverageAnalyser;
        private readonly IStrykerOptions _options;
        private readonly IMutationProcess _mutationProcess;

        public MutationTestProcess(MutationTestInput mutationTestInput,
            IReporter reporter,
            IMutationTestExecutor mutationTestExecutor,
            IMutantOrchestrator orchestrator = null,
            IFileSystem fileSystem = null,
            IMutantFilter mutantFilter = null,
            ICoverageAnalyser coverageAnalyser = null,
            IStrykerOptions options = null)
        {
            Input = mutationTestInput;
            _projectContents = mutationTestInput.ProjectInfo.ProjectContents;
            _reporter = reporter;
            _options = options;
            _mutationTestExecutor = mutationTestExecutor;
            _logger = ApplicationLogging.LoggerFactory.CreateLogger<MutationTestProcess>();
            _coverageAnalyser = coverageAnalyser ?? new CoverageAnalyser(_options, _mutationTestExecutor, mutationTestInput);

            _mutationProcess = new MutationProcess(Input, orchestrator, fileSystem, _options, mutantFilter, _reporter);
        }

        public void Mutate()
        {
            _mutationProcess.Mutate();
        }

        public void FilterMutants()
        {
            _mutationProcess.FilterMutants();
        }

        public StrykerRunResult Test(IEnumerable<Mutant> mutantsToTest)
        {
            if (!MutantsToTest(mutantsToTest))
            {
                return new StrykerRunResult(_options, double.NaN);
            }

            TestMutants(mutantsToTest);
            _mutationTestExecutor.TestRunner.Dispose();

            return new StrykerRunResult(_options, _projectContents.ToReadOnlyInputComponent().GetMutationScore());
        }

        private void TestMutants(IEnumerable<Mutant> mutantsToTest)
        {
            var mutantGroups = BuildMutantGroupsForTest(mutantsToTest.ToList());

            if (_options.TracedMutants != null && _options.TracedMutants.Any())
            {
                if (_options.DevMode)
                {
                    _logger.LogInformation(
                        $"We will only tests the mutant(s) configured for tracing({string.Join(',', _options.TracedMutants)}).");
                    mutantGroups = mutantGroups.Select(t => t.Where(m => _options.TracedMutants.Contains(m.Id)).ToList()).Where(t => t.Count>0);

                }
                else
                {
                    _logger.LogInformation(
                        $"We will only tests groups which contains mutant(s) configured for tracing({string.Join(',', _options.TracedMutants)}).");
                    mutantGroups = mutantGroups.Where(l => l.Any(t => _options.TracedMutants.Contains(t.Id)));
                }
            }

            var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = _options.ConcurrentTestrunners };

            Parallel.ForEach(mutantGroups, parallelOptions, TestOneMutantGroup);
        }

        private void TestOneMutantGroup(List<Mutant> mutants)
        {
            var reportedMutants = new HashSet<Mutant>();

            _mutationTestExecutor.Test(mutants, Input.TimeoutMs,
                (testedMutants, failedTests, ranTests, timedOutTest) =>
            {
                var continueTestRun = !_options.Optimizations.HasFlag(OptimizationFlags.AbortTestOnKill);
                foreach (var mutant in testedMutants)
                {
                    mutant.AnalyzeTestRun(failedTests, ranTests, timedOutTest);

                    if (mutant.ResultStatus == MutantStatus.NotRun)
                    {
                        continueTestRun = true; // Not all mutants in this group were tested so we continue
                    }

                    OnMutantTested(mutant, reportedMutants); // Report on mutant that has been tested
                }

                return continueTestRun;
            });

            foreach (var MutantAsync in mutants)
            {
                if (MutantAsync.ResultStatus == MutantStatus.NotRun)
                {
                    _logger.LogWarning($"Mutation {MutantAsync.Id} was not fully tested.");
                }

                OnMutantTested(MutantAsync, reportedMutants);
            }
        }

        private void OnMutantTested(Mutant mutant, HashSet<Mutant> reportedMutants)
        {
            if (mutant.ResultStatus != MutantStatus.NotRun && !reportedMutants.Contains(mutant))
            {
                _logger.LogDebug("Mutant {Ø} has been tested, result is {1}.", mutant.DisplayName, mutant.ResultStatus);
                _reporter.OnMutantTested(mutant);
                reportedMutants.Add(mutant);
            }
        }

        private static bool MutantsToTest(IEnumerable<Mutant> mutantsToTest)
        {
            if (!mutantsToTest.Any())
            {
                return false;
            }
            if (mutantsToTest.Any(x => x.ResultStatus != MutantStatus.NotRun))
            {
                throw new GeneralStrykerException("Only mutants to run should be passed to the mutation test process. If you see this message please report an issue.");
            }

            return true;
        }

        private IEnumerable<List<Mutant>> BuildMutantGroupsForTest(IReadOnlyCollection<Mutant> mutantsNotRun)
        {
            if (_options.Optimizations.HasFlag(OptimizationFlags.DisableTestMix) || !_options.Optimizations.HasFlag(OptimizationFlags.CoverageBasedTest))
            {
                return mutantsNotRun.Select(x => new List<Mutant> { x });
            }

            var blocks = new List<List<Mutant>>(mutantsNotRun.Count);
            var mutantsToGroup = mutantsNotRun.ToList();
            // we deal with mutants needing full testing first
            blocks.AddRange(mutantsToGroup.Where(m => m.MustRunAgainstAllTests).Select(m => new List<Mutant> { m }));
            mutantsToGroup.RemoveAll(m => m.MustRunAgainstAllTests);
            var testsCount = mutantsToGroup.SelectMany(m => m.CoveringTests.GetList()).Distinct().Count();
            mutantsToGroup = mutantsToGroup.OrderByDescending(m => m.CoveringTests.Count).ToList();
            for (var i = 0; i < mutantsToGroup.Count; i++)
            {
                var usedTests = mutantsToGroup[i].CoveringTests.GetList().ToList();
                var nextBlock = new List<Mutant> { mutantsToGroup[i] };
                for (var j = i + 1; j < mutantsToGroup.Count; j++)
                {
                    if (mutantsToGroup[j].CoveringTests.Count + usedTests.Count > testsCount ||
                        mutantsToGroup[j].CoveringTests.ContainsAny(usedTests))
                    {
                        continue;
                    }

                    nextBlock.Add(mutantsToGroup[j]);
                    usedTests.AddRange(mutantsToGroup[j].CoveringTests.GetList());
                    mutantsToGroup.RemoveAt(j--);
                }

                blocks.Add(nextBlock);
            }

            _logger.LogDebug($"Mutations will be tested in {blocks.Count} test runs, instead of {mutantsNotRun.Count}.");
            return blocks;
        }

        public void GetCoverage()
        {
            _coverageAnalyser.DetermineTestCoverage();
        }
    }
}
