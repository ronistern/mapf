﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics;
using System.Linq;

using ExtensionMethods;

namespace CPF_experiment
{
    /// <summary>
    /// Merges agents if they conflict more times than the given threshold in the CT nodes from the root to the current CT nodes only.
    /// </summary>
    public class CBS_LocalConflicts : ICbsSolver
    {
        /// <summary>
        /// The key of the constraints list used for each CBS node
        /// </summary>
        public static readonly string CONSTRAINTS = "constraints";
        /// <summary>
        /// The key of the must constraints list used for each CBS node
        /// </summary>
        public static readonly string MUST_CONSTRAINTS = "must constraints";
        /// <summary>
        /// The key of the internal CAT for CBS, used to favor A* nodes that have fewer conflicts with other routes during tie-breaking.
        /// Also used to indicate that CBS is running.
        /// </summary>
        public static readonly string CAT = "CBS CAT";

        protected ProblemInstance instance;
        public OpenList openList;
        /// <summary>
        /// Might as well be a HashSet. We don't need to retrive from it.
        /// </summary>
        public Dictionary<CbsNode, CbsNode> closedList;
        protected int highLevelExpanded;
        protected int highLevelGenerated;
        protected int closedListHits;
        protected int partialExpansions;
        protected int bypasses;
        protected int pruningSuccesses;
        protected int pruningFailures;
        protected int nodesExpandedWithGoalCost;
        protected int lookAheadNodesCreated;
        protected int conflictsBypassed;
        protected int cardinalConflictSplits;
        protected int semiCardinalConflictSplits;
        protected int nonCardinalConflictSplits;
        public int mddsBuilt;
        protected int nodesPushedBack;
        protected int restarts;
        // TODO: Count shuffles
        protected int accHLExpanded;
        protected int accHLGenerated;
        protected int accClosedListHits;
        protected int accPartialExpansions;
        protected int accBypasses;
        protected int accPruningSuccesses;
        protected int accPruningFailures;
        protected int accNodesExpandedWithGoalCost;
        protected int accLookAheadNodesCreated;
        protected int accConflictsBypassed;
        protected int accCardinalConflictSplits;
        protected int accSemiCardinalConflictSplits;
        protected int accNonCardinalConflictSplits;
        protected int accMddsBuilt;
        protected int accNodesPushedBack;
        protected int accRestarts;

        public int totalCost;
        protected int solutionDepth;
        public Run runner;
        protected CbsNode goalNode;
        protected Plan solution;
        /// <summary>
        /// Nodes with with a higher cost aren't generated
        /// </summary>
        protected int maxCost;
        /// <summary>
        /// Search is stopped when the minimum cost passes the target
        /// </summary>
        public int targetCost {set; get;}
        /// <summary>
        /// Search is stopped when the low level generated nodes count exceeds the cap
        /// </summary>
        public int lowLevelGeneratedCap { set; get; }
        /// <summary>
        /// Search is stopped when the millisecond count exceeds the cap
        /// </summary>
        public int milliCap { set; get; }
        protected ICbsSolver solver;
        protected ICbsSolver singleAgentSolver;
        public int mergeThreshold;
        public int conflictRange;
        //public bool rangeConstraint;
        public Run.ConstraintPolicy constraintPolicy;

        /// <summary>
        /// TODO: Shouldn't this be called minTimeStep?
        /// </summary>
        protected int minDepth;
        protected int maxSizeGroup;
        protected int accMaxSizeGroup;
        /// <summary>
        /// Used to know when to clear problem parameters.
        /// </summary>
        public bool topMost;
        public bool doShuffle;
        public BypassStrategy bypassStrategy;
        public bool doMalte;
        public ConflictChoice conflictChoice;
        public bool tieBreakForMoreConflictsOnly;
        public bool useMddH;
        public Dictionary<CostTreeNode, bool> costTreeMDDResults;
        public int lookaheadMaxExpansions;
        public enum ConflictChoice : byte
        {
            FIRST = 0,
            MOST_CONFLICTING,
            CARDINAL_MDD,
            CARDINAL_LOOKAHEAD
        }
        public enum BypassStrategy : byte
        {
            NONE = 0,
            FIRST_FIT_LOOKAHEAD,
            BEST_FIT_LOOKAHEAD
        }
        private bool mergeCausesRestart;


        public CBS_LocalConflicts(ICbsSolver singleAgentSolver, ICbsSolver generalSolver, int mergeThreshold = -1,
                                  bool doShuffle = false, BypassStrategy bypassStrategy = BypassStrategy.NONE, bool doMalte = false,
                                  ConflictChoice conflictChoice = ConflictChoice.FIRST,
                                  bool justbreakForConflicts = false, bool useMddHeuristic = false,
                                  int lookaheadMaxExpansions = int.MaxValue, bool mergeCausesRestart = false, int conflictRange = 0, Run.ConstraintPolicy constraintPolicy = Run.ConstraintPolicy.Single)
        {
            this.closedList = new Dictionary<CbsNode, CbsNode>();
            this.openList = new OpenList(this);
            this.mergeThreshold = mergeThreshold;
            this.conflictRange = conflictRange;
            //this.rangeConstraint = rangeConstraint;
            this.constraintPolicy = constraintPolicy;
            this.solver = generalSolver;
            this.singleAgentSolver = singleAgentSolver;
            this.doShuffle = doShuffle;
            //this.doShuffle = doShuffle || useMddHeuristic;
            this.bypassStrategy = bypassStrategy;
            this.doMalte = doMalte;
            this.conflictChoice = conflictChoice;
            this.tieBreakForMoreConflictsOnly = justbreakForConflicts;
            this.useMddH = useMddHeuristic;
            this.lookaheadMaxExpansions = lookaheadMaxExpansions;
            this.mergeCausesRestart = mergeCausesRestart;

            if (useMddHeuristic)
            {
                costTreeMDDResults = new Dictionary<CostTreeNode, bool>();
            }
        }
        
        /// <summary>
        /// 
        /// </summary>
        /// <param name="problemInstance"></param>
        /// <param name="minDepth"></param>
        /// <param name="runner"></param>
        /// <param name="minCost">Not taken into account</param>
        public virtual void Setup(ProblemInstance problemInstance, int minDepth, Run runner, int minCost = -1)
        {
            this.instance = problemInstance;
            this.runner = runner;
            this.ClearPrivateStatistics();
            this.totalCost = 0;
            this.solutionDepth = -1;
            this.targetCost = int.MaxValue;
            this.lowLevelGeneratedCap = int.MaxValue;
            this.milliCap = int.MaxValue;
            this.goalNode = null;
            this.solution = null;
            if (problemInstance.parameters.ContainsKey(IndependenceDetection.MAXIMUM_COST_KEY))
                this.maxCost = (int)(problemInstance.parameters[IndependenceDetection.MAXIMUM_COST_KEY]);
            else
                this.maxCost = int.MaxValue;
            this.topMost = this.SetGlobals();

            this.minDepth = minDepth;
            CbsNode root = new CbsNode(instance.m_vAgents.Length, this.solver, this.singleAgentSolver, this); // Problem instance and various strategy data is all passed under 'this'.
            // Solve the root node
            bool solved = root.Solve(minDepth);
            
            if (solved && root.totalCost <= this.maxCost)
            {
                this.openList.Add(root);
                this.highLevelGenerated++;
                this.closedList.Add(root, root);
            }
        }

        public virtual void Setup(ProblemInstance problemInstance, Run runner)
        {
            this.Setup(problemInstance, 0, runner);
        }

        public void SetHeuristic(HeuristicCalculator heuristic)
        {
            this.solver.SetHeuristic(heuristic);
        }

        public HeuristicCalculator GetHeuristic()
        {
            return this.solver.GetHeuristic();
        }

        public Dictionary<int, int> GetExternalConflictCounts()
        {
            throw new NotImplementedException(); // For now. Also need to take care of generalised goal nodes!
        }

        public Dictionary<int, List<int>> GetConflictTimes()
        {
            throw new NotImplementedException(); // For now. Also need to take care of generalised goal nodes!
        }

        public Dictionary<int, List<int>> GetConflictTimesBias()
        {
            throw new NotImplementedException(); // For now. Also need to take care of generalised goal nodes!
        }

        public ProblemInstance GetProblemInstance()
        {
            return this.instance;
        }

        public void Clear()
        {
            this.openList.Clear();
            this.closedList.Clear();
            this.solver.Clear();
            // Statistics are reset on Setup.
        }

        public virtual string GetName() 
        {
            string lowLevelSolvers;
            if (mergeThreshold == -1 || Object.ReferenceEquals(this.singleAgentSolver, this.solver))
                lowLevelSolvers = "(" + this.singleAgentSolver + ")";
            else
                lowLevelSolvers = "(single:" + singleAgentSolver + " multi:" + solver + ")";
            string variants = "";
            if (this.doShuffle)
                variants += " with shuffle";
            if (this.bypassStrategy == BypassStrategy.FIRST_FIT_LOOKAHEAD)
            {
                if (this.lookaheadMaxExpansions != int.MaxValue)
                    variants += " with first fit adoption max expansions: " + this.lookaheadMaxExpansions;
                else
                    variants += " with first fit adoption max expansions: $\\infty$"; // LaTeX inifinity symbol
            }
            if (this.bypassStrategy == BypassStrategy.BEST_FIT_LOOKAHEAD)
            {
                if (this.lookaheadMaxExpansions == int.MaxValue)
                    variants += " with infinite lookahead best fit adoption";
                else
                    variants += " with " + this.lookaheadMaxExpansions.ToString() + " lookahead best fit adoption";
            }
            if (this.doMalte)
                variants += " with Malte";
            if (this.conflictChoice == ConflictChoice.FIRST)
                variants += " choosing the first conflict in CBS nodes";
            else if (this.conflictChoice == ConflictChoice.CARDINAL_MDD)
                variants += " choosing cardinal conflicts using MDD";
            else if (this.conflictChoice == ConflictChoice.CARDINAL_LOOKAHEAD)
                variants += " choosing cardinal conflicts using lookahead";
            if (this.tieBreakForMoreConflictsOnly == true)
                variants += " without smart tie breaking";
            if (this.useMddH == true)
                variants += " with MDD heuristic";
            if (this.mergeCausesRestart == true)
                variants += " with merge&restart";

            if (mergeThreshold == -1)
                return "Basic-CBS/" + lowLevelSolvers + variants;
            return "MA-CBS-Local-" + mergeThreshold + "/" + lowLevelSolvers + variants;
        }

        public override string ToString()
        {
            return GetName();
        }

        public int GetSolutionCost() { return this.totalCost; }

        protected void ClearPrivateStatistics()
        {
            this.highLevelExpanded = 0;
            this.highLevelGenerated = 0;
            this.closedListHits = 0;
            this.partialExpansions = 0;
            this.bypasses = 0;
            this.pruningSuccesses = 0;
            this.pruningFailures = 0;
            this.nodesExpandedWithGoalCost = 0;
            this.lookAheadNodesCreated = 0;
            this.conflictsBypassed = 0;
            this.cardinalConflictSplits = 0;
            this.semiCardinalConflictSplits = 0;
            this.nonCardinalConflictSplits = 0;
            this.mddsBuilt = 0;
            this.nodesPushedBack = 0;
            this.restarts = 0;
            this.maxSizeGroup = 1;
        }

        public virtual void OutputStatisticsHeader(TextWriter output)
        {
            output.Write(this.ToString() + " Expanded (HL)");
            output.Write(Run.RESULTS_DELIMITER);
            output.Write(this.ToString() + " Generated (HL)");
            output.Write(Run.RESULTS_DELIMITER);
            output.Write(this.ToString() + " Closed List Hits (HL)");
            output.Write(Run.RESULTS_DELIMITER);
            output.Write(this.ToString() + " Partial Expansions (HL)");
            output.Write(Run.RESULTS_DELIMITER);
            output.Write(this.ToString() + " Adoptions (HL)");
            output.Write(Run.RESULTS_DELIMITER);
            output.Write(this.ToString() + " Pruning Successes (HL)");
            output.Write(Run.RESULTS_DELIMITER);
            output.Write(this.ToString() + " Pruning Failures (HL)");
            output.Write(Run.RESULTS_DELIMITER);
            output.Write(this.ToString() + " Nodes Expanded With Goal Cost (HL)");
            output.Write(Run.RESULTS_DELIMITER);
            output.Write(this.ToString() + " Look Ahead Nodes Created (HL)");
            output.Write(Run.RESULTS_DELIMITER);
            output.Write(this.ToString() + " Conflicts Bypassed With Adoption (HL)");
            output.Write(Run.RESULTS_DELIMITER);
            output.Write(this.ToString() + " Cardinal Conflict Splits (HL)");
            output.Write(Run.RESULTS_DELIMITER);
            output.Write(this.ToString() + " Semi-Cardinal Conflict Splits (HL)");
            output.Write(Run.RESULTS_DELIMITER);
            output.Write(this.ToString() + " Non-Cardinal Conflict Splits (HL)");
            output.Write(Run.RESULTS_DELIMITER);
            output.Write(this.ToString() + " MDDs Built (HL)");
            output.Write(Run.RESULTS_DELIMITER);
            output.Write(this.ToString() + " Nodes Pushed Back (HL)");
            output.Write(Run.RESULTS_DELIMITER);
            output.Write(this.ToString() + " Restarts (HL)");
            output.Write(Run.RESULTS_DELIMITER);
            output.Write(this.ToString() + " Max Group Size (HL)");
            output.Write(Run.RESULTS_DELIMITER);

            this.solver.OutputStatisticsHeader(output);
            if (Object.ReferenceEquals(this.singleAgentSolver, this.solver) == false)
                this.singleAgentSolver.OutputStatisticsHeader(output);

            this.openList.OutputStatisticsHeader(output);
        }

        public virtual void OutputStatistics(TextWriter output)
        {
            Console.WriteLine("Total Expanded Nodes (High-Level): {0}", this.GetHighLevelExpanded());
            Console.WriteLine("Total Generated Nodes (High-Level): {0}", this.GetHighLevelGenerated());
            Console.WriteLine("Closed List Hits (High-Level): {0}", this.closedListHits);
            Console.WriteLine("Partial Expansions (High-Level): {0}", this.partialExpansions);
            Console.WriteLine("Adoptions (High-Level): {0}", this.bypasses);
            Console.WriteLine("Pruning successes (High-Level): {0}", this.pruningSuccesses);
            Console.WriteLine("Pruning failures (High-Level): {0}", this.pruningFailures);
            Console.WriteLine("Nodes expanded with goal cost (High-Level): {0}", this.nodesExpandedWithGoalCost);
            Console.WriteLine("Look ahead nodes created (High-Level): {0}", this.lookAheadNodesCreated);
            Console.WriteLine("Conflicts Bypassed With Adoption (High-Level): {0}", this.conflictsBypassed);
            Console.WriteLine("Cardinal Conflicts Splits (High-Level): {0}", this.cardinalConflictSplits);
            Console.WriteLine("Semi-Cardinal Conflicts Splits (High-Level): {0}", this.semiCardinalConflictSplits);
            Console.WriteLine("Non-Cardinal Conflicts Splits (High-Level): {0}", this.nonCardinalConflictSplits);
            Console.WriteLine("MDDs Built (High-Level): {0}", this.mddsBuilt);
            Console.WriteLine("Nodes Pushed Back (High-Level): {0}", this.nodesPushedBack);
            Console.WriteLine("Restarts (High-Level): {0}", this.restarts);
            Console.WriteLine("Max Group Size (High-Level): {0}", this.maxSizeGroup);

            output.Write(this.highLevelExpanded + Run.RESULTS_DELIMITER);
            output.Write(this.highLevelGenerated + Run.RESULTS_DELIMITER);
            output.Write(this.closedListHits + Run.RESULTS_DELIMITER);
            output.Write(this.partialExpansions + Run.RESULTS_DELIMITER);
            output.Write(this.bypasses + Run.RESULTS_DELIMITER);
            output.Write(this.pruningSuccesses + Run.RESULTS_DELIMITER);
            output.Write(this.pruningFailures + Run.RESULTS_DELIMITER);
            output.Write(this.nodesExpandedWithGoalCost + Run.RESULTS_DELIMITER);
            output.Write(this.lookAheadNodesCreated + Run.RESULTS_DELIMITER);
            output.Write(this.conflictsBypassed + Run.RESULTS_DELIMITER);
            output.Write(this.cardinalConflictSplits + Run.RESULTS_DELIMITER);
            output.Write(this.semiCardinalConflictSplits + Run.RESULTS_DELIMITER);
            output.Write(this.nonCardinalConflictSplits + Run.RESULTS_DELIMITER);
            output.Write(this.mddsBuilt + Run.RESULTS_DELIMITER);
            output.Write(this.nodesPushedBack + Run.RESULTS_DELIMITER);
            output.Write(this.restarts + Run.RESULTS_DELIMITER);
            output.Write(this.maxSizeGroup + Run.RESULTS_DELIMITER);

            this.solver.OutputAccumulatedStatistics(output);
            if (Object.ReferenceEquals(this.singleAgentSolver, this.solver) == false)
                this.singleAgentSolver.OutputAccumulatedStatistics(output);

            this.openList.OutputStatistics(output);
        }

        public virtual int NumStatsColumns
        {
            get
            {
                int numSolverStats = this.solver.NumStatsColumns;
                if (Object.ReferenceEquals(this.singleAgentSolver, this.solver) == false)
                    numSolverStats += this.singleAgentSolver.NumStatsColumns;
                return 17 + numSolverStats + this.openList.NumStatsColumns;
            }
        }

        public virtual void ClearStatistics()
        {
            if (this.topMost)
            {
                this.solver.ClearAccumulatedStatistics(); // Is this correct? Or is it better not to do it?
                if (Object.ReferenceEquals(this.singleAgentSolver, this.solver) == false)
                    this.singleAgentSolver.ClearAccumulatedStatistics();
            }
            this.ClearPrivateStatistics();
            this.openList.ClearStatistics();
        }

        public virtual void ClearAccumulatedStatistics()
        {
            this.accHLExpanded = 0;
            this.accHLGenerated = 0;
            this.accClosedListHits = 0;
            this.accPartialExpansions = 0;
            this.accBypasses = 0;
            this.accPruningSuccesses = 0;
            this.accPruningFailures = 0;
            this.accNodesExpandedWithGoalCost = 0;
            this.accLookAheadNodesCreated = 0;
            this.accConflictsBypassed = 0;
            this.accCardinalConflictSplits = 0;
            this.accSemiCardinalConflictSplits = 0;
            this.accNonCardinalConflictSplits = 0;
            this.accMddsBuilt = 0;
            this.accNodesPushedBack = 0;
            this.accRestarts = 0;
            this.accMaxSizeGroup = 1;

            this.solver.ClearAccumulatedStatistics();
            if (Object.ReferenceEquals(this.singleAgentSolver, this.solver) == false)
                this.singleAgentSolver.ClearAccumulatedStatistics();

            this.openList.ClearAccumulatedStatistics();
        }

        public virtual void AccumulateStatistics()
        {
            this.accHLExpanded += this.highLevelExpanded;
            this.accHLGenerated += this.highLevelGenerated;
            this.accClosedListHits += this.closedListHits;
            this.accPartialExpansions += this.partialExpansions;
            this.accBypasses += this.bypasses;
            this.accPruningSuccesses += this.pruningSuccesses;
            this.accPruningFailures += this.pruningFailures;
            this.accNodesExpandedWithGoalCost += this.nodesExpandedWithGoalCost;
            this.accLookAheadNodesCreated += this.lookAheadNodesCreated;
            this.accConflictsBypassed += this.conflictsBypassed;
            this.accCardinalConflictSplits += this.cardinalConflictSplits;
            this.accSemiCardinalConflictSplits += this.semiCardinalConflictSplits;
            this.accNonCardinalConflictSplits += this.nonCardinalConflictSplits;
            this.accMddsBuilt += this.mddsBuilt;
            this.accNodesPushedBack += this.nodesPushedBack;
            this.accRestarts += this.restarts;
            this.accMaxSizeGroup = Math.Max(this.accMaxSizeGroup, this.maxSizeGroup);

            // this.solver statistics are accumulated every time it's used.

            this.openList.AccumulateStatistics();
        }

        public virtual void OutputAccumulatedStatistics(TextWriter output)
        {
            Console.WriteLine("{0} Accumulated Expanded Nodes (High-Level): {1}", this, this.accHLExpanded);
            Console.WriteLine("{0} Accumulated Generated Nodes (High-Level): {1}", this, this.accHLGenerated);
            Console.WriteLine("{0} Accumulated Closed List Hits (High-Level): {1}", this, this.accClosedListHits);
            Console.WriteLine("{0} Accumulated Partial Expansions (High-Level): {1}", this, this.accPartialExpansions);
            Console.WriteLine("{0} Accumulated Adoptions (High-Level): {1}", this, this.accBypasses);
            Console.WriteLine("{0} Accumulated Pruning Successes (High-Level): {1}", this, this.accPruningSuccesses);
            Console.WriteLine("{0} Accumulated Pruning Failures (High-Level): {1}", this, this.accPruningFailures);
            Console.WriteLine("{0} Accumulated Nodes Expanded With Goal Cost (High-Level): {1}", this, this.accNodesExpandedWithGoalCost);
            Console.WriteLine("{0} Accumulated Look Ahead Nodes Created (High-Level): {1}", this, this.accNodesExpandedWithGoalCost);
            Console.WriteLine("{0} Accumulated Conflicts Bypassed With Adoption (High-Level): {1}", this, this.accConflictsBypassed);
            Console.WriteLine("{0} Accumulated Cardinal Conflicts Splits (High-Level): {1}", this.accCardinalConflictSplits);
            Console.WriteLine("{0} Accumulated Semi-Cardinal Conflicts Splits (High-Level): {1}", this.accSemiCardinalConflictSplits);
            Console.WriteLine("{0} Accumulated Non-Cardinal Conflicts Splits (High-Level): {1}", this.accNonCardinalConflictSplits);
            Console.WriteLine("{0} Accumulated MDDs Built (High-Level): {1}", this.accMddsBuilt);
            Console.WriteLine("{0} Accumulated Nodes Pushed Back (High-Level): {1}", this.accNodesPushedBack);
            Console.WriteLine("{0} Accumulated Restarts (High-Level): {1}", this.accRestarts);
            Console.WriteLine("{0} Max Group Size (High-Level): {1}", this, this.accMaxSizeGroup);

            output.Write(this.accHLExpanded + Run.RESULTS_DELIMITER);
            output.Write(this.accHLGenerated + Run.RESULTS_DELIMITER);
            output.Write(this.accClosedListHits + Run.RESULTS_DELIMITER);
            output.Write(this.accPartialExpansions + Run.RESULTS_DELIMITER);
            output.Write(this.accBypasses + Run.RESULTS_DELIMITER);
            output.Write(this.accPruningSuccesses + Run.RESULTS_DELIMITER);
            output.Write(this.accPruningFailures + Run.RESULTS_DELIMITER);
            output.Write(this.accNodesExpandedWithGoalCost + Run.RESULTS_DELIMITER);
            output.Write(this.accLookAheadNodesCreated + Run.RESULTS_DELIMITER);
            output.Write(this.accConflictsBypassed + Run.RESULTS_DELIMITER);
            output.Write(this.accCardinalConflictSplits + Run.RESULTS_DELIMITER);
            output.Write(this.accSemiCardinalConflictSplits + Run.RESULTS_DELIMITER);
            output.Write(this.accNonCardinalConflictSplits + Run.RESULTS_DELIMITER);
            output.Write(this.accMddsBuilt + Run.RESULTS_DELIMITER);
            output.Write(this.accNodesPushedBack + Run.RESULTS_DELIMITER);
            output.Write(this.accRestarts + Run.RESULTS_DELIMITER);
            output.Write(this.accMaxSizeGroup + Run.RESULTS_DELIMITER);

            this.solver.OutputAccumulatedStatistics(output);
            if (Object.ReferenceEquals(this.singleAgentSolver, this.solver) == false)
                this.singleAgentSolver.OutputAccumulatedStatistics(output);

            this.openList.OutputAccumulatedStatistics(output);
        }

        public bool debug = false;
        private bool equivalenceWasOn;

        /// <summary>
        /// 
        /// </summary>
        /// <returns>Whether this is the top-most CBS</returns>
        protected bool SetGlobals()
        {
            this.equivalenceWasOn = (AgentState.EquivalenceOverDifferentTimes == true);
            AgentState.EquivalenceOverDifferentTimes = false;

            if (this.instance.parameters.ContainsKey(CBS_LocalConflicts.CAT) == false) // Top-most CBS solver
            {
                this.instance.parameters[CBS_LocalConflicts.CAT] = new Dictionary_U<TimedMove, int>(); // Dictionary_U values are actually lists of ints.
                this.instance.parameters[CBS_LocalConflicts.CONSTRAINTS] = new HashSet_U<CbsConstraint>();
                if (this.doMalte && this.instance.parameters.ContainsKey(CBS_LocalConflicts.MUST_CONSTRAINTS) == false)
                    this.instance.parameters[CBS_LocalConflicts.MUST_CONSTRAINTS] = new HashSet_U<CbsConstraint>();
                return true;
            }
            else
                return false;
        }

        protected void CleanGlobals()
        {
            if (this.equivalenceWasOn)
                AgentState.EquivalenceOverDifferentTimes = true;
            if (this.topMost) // Clear problem parameters. Done for clarity only, since the Union structures are expected to be empty at this point.
            {
                this.instance.parameters.Remove(CBS_LocalConflicts.CAT);
                this.instance.parameters.Remove(CBS_LocalConflicts.CONSTRAINTS);
                // Don't remove must constraints:
                // A) It usually wasn't CBS that added them (they're only used for Malte's variant).
                // B) Must constraints only appear in temporary problems. There's no danger of leaking them to other solvers.
                // C) We don't have the information to re-create them later.
            }
        }

        public bool Solve()
        {
            //this.SetGlobals(); // Again, because we might be resuming a search that was stopped.

            int initialEstimate = 0;
            if (openList.Count > 0)
                initialEstimate = ((CbsNode)openList.Peek()).totalCost;

            int maxExpandedNodeCostPlusH = -1;
            int currentCost = -1;

            while (openList.Count > 0)
            {
                // Check if max time has been exceeded
                if (runner.ElapsedMilliseconds() > Constants.MAX_TIME)
                {
                    this.totalCost = Constants.TIMEOUT_COST;
                    Console.WriteLine("Out of time");
                    this.solutionDepth = ((CbsNode)openList.Peek()).totalCost - initialEstimate; // A minimum estimate
                    this.Clear(); // Total search time exceeded - we're not going to resume this search.
                    this.CleanGlobals();
                    return false;
                }
                var currentNode = (CbsNode)openList.Remove();

                currentNode.ChooseConflict();

                // A cardinal conflict may have been found, increasing the h of the node.
                // Check if the node needs to be pushed back into the open list.
                if (this.openList.Count != 0 &&
                    currentNode.f > ((CbsNode)this.openList.Peek()).f)
                {
                    if (this.debug)
                        Debug.Print("Pushing back the node into the open list with an increased h.");
                    this.openList.Add(currentNode);
                    this.nodesPushedBack++;
                    continue;
                    // Notice that even though we may iterate over conflicts later,
                    // even there is a conflict that we can identify as cardinal,
                    // then the first conflict chosen _will_ be cardinal, so this is
                    // the only place we need allow pushing nodes back.
                    // We may discover cardinal conflicts in hindsight later, but there
                    // would be no point in pushing their node back at that point,
                    // as we would've already made the split by then.
                }

                this.addToGlobalConflictCount(currentNode.GetConflict()); // TODO: Make CBS_GlobalConflicts use nodes that do this automatically after choosing a conflict

                if (debug)
                    currentNode.Print();

                // Lazy MDD stage
                if (this.useMddH)
                {
                    if (currentNode.h == 0 && // Otherwise the node's h was already computed. It may have been computed if h is zero, but then the lookup will find the h quickly. This is just a minor optimization.
                        this.openList.Count != 0 && // Otherwise there's no need to pay for delaying expansion
                        currentNode.f == ((CbsNode)this.openList.Peek()).f) // We can only add 1 to the h so a push back can only happen if the F of the next node is equal to the current one's before starting.
                    // TODO: Generalize DynamicLazyOpenList to fit CBS instead of this logic duplication
                    {
                        bool solvable = this.MDDHeuristic(currentNode);
                        if (solvable)
                        {
                            if (this.debug)
                                Debug.Print("MDD thinks this node may be solvable with its current costs");
                            currentNode.h = 0; // Just for consistancy
                        }
                        else
                        {
                            if (this.debug)
                            {
                                Debug.Print("MDD proved no solution exists for the node's configuration of costs.");
                                //Debug.Print("Using MDD's least conflicting configuration.");
                                // NOT IMPLEMENTED BUT POSSIBLE.
                            }
                            currentNode.h = 1;
                            var next = (CbsNode)this.openList.Peek();
                            if (currentNode.CompareTo(next) == 1) // Must be true - they were equal before we enlarged the h.
                            {
                                if (this.debug)
                                    Debug.Print("Pushing back the node into the open list with an increased h.");
                                this.openList.Add(currentNode);
                                this.nodesPushedBack++;
                                continue;
                            }
                            else
                            {
                                if (this.debug)
                                    Debug.Print("MDD proved this node isn't solvable with its current costs, but the next node isn't smaller than the current node with its improved h, so we're not pushing it back.");
                            }
                        }
                    }
                    else
                    {
                        if (debug)
                        {
                            if (currentNode.h != 0)
                                Debug.Print("Not building an MDD on this node, h was already computed on it");
                            else if (this.openList.Count == 0)
                                Debug.Print("Not building an MDD on this node, it's the only one in the open list");
                            else
                                Debug.Print("Not building an MDD on this node, we can't raise its h enough to push it back");
                        }
                    }
                }

                // Shuffle stage:
                if (this.doShuffle)
                    this.Shuffle(currentNode);

                if (currentNode.totalCost > currentCost) // Needs to be here because the goal may have a cost unseen before
                {
                    currentCost = currentNode.totalCost;
                    this.nodesExpandedWithGoalCost = 0;
                }
                else if (currentNode.totalCost == currentCost) // check needed because macbs node cost isn't exactly monotonous
                {
                    this.nodesExpandedWithGoalCost++;
                }

                // Check if node is the goal
                if (currentNode.GoalTest())
                {
                    //Debug.Assert(currentNode.totalCost >= maxExpandedNodeCostPlusH, "CBS goal node found with lower cost than the max cost node ever expanded: " + currentNode.totalCost + " < " + maxExpandedNodeCostPlusH);
                    // This is subtle, but MA-CBS may expand nodes in a non non-decreasing order:
                    // If a node with a non-optimal constraint is expanded and we decide to merge the agents,
                    // the resulting node can have a lower cost than before, since we ignore the non-optimal constraint
                    // because the conflict it addresses is between merged nodes.
                    // The resulting lower-cost node will have other constraints, that will raise the cost of its children back to at least its original cost,
                    // since the node with the non-optimal constraint was only expanded because its competitors that had an optimal
                    // constraint to deal with the same conflict apparently found the other conflict that I promise will be found,
                    // and so their cost was not smaller than this sub-optimal node.
                    // To make MA-CBS costs non-decreasing, we can choose not to ignore constraints that deal with conflicts between merged nodes.
                    // That way, the sub-optimal node will find a sub-optimal merged solution and get a high cost that will push it deep into the open list.
                    // But the cost would be to create a possibly sub-optimal merged solution where an optimal solution could be found instead, and faster,
                    // since constraints make the low-level heuristic perform worse.
                    // For an example for this subtle case happening, see problem instance 63 of the random grid with 4 agents,
                    // 55 grid cells and 9 obstacles.

                    if (debug)
                        Debug.WriteLine("-----------------");
                    this.totalCost = currentNode.totalCost;
                    this.solutionDepth = this.totalCost - initialEstimate;
                    this.goalNode = currentNode; // Saves the single agent plans and costs
                    // The joint plan is calculated on demand.
                    this.Clear(); // Goal found - we're not going to resume this search
                    this.CleanGlobals();
                    return true;
                }

                if (currentNode.totalCost >= this.targetCost || // Node is good enough
                    //(this.targetCost != int.MaxValue &&
                     //this.lowLevelGenerated > Math.Pow(Constants.NUM_ALLOWED_DIRECTIONS, this.instance.m_vAgents.Length))
                    this.solver.GetAccumulatedGenerated() > this.lowLevelGeneratedCap || // Stop because this is taking too long.
                                                                                         // We're looking at _generated_ low level nodes since that's an indication to the amount of work done,
                                                                                         // while expanded nodes is an indication of the amount of good work done.
                                                                                         // b**k is the maximum amount of nodes we'll generate if we expand this node with A*.
                    (this.milliCap != int.MaxValue && // (This check is much cheaper than the method call)
                     this.runner.ElapsedMilliseconds() > this.milliCap)) // Search is taking too long.
                {
                    if (debug)
                        Debug.WriteLine("-----------------");
                    this.totalCost = maxExpandedNodeCostPlusH; // This is the min possible cost so far.
                    this.openList.Add(currentNode); // To be able to continue the search later
                    this.CleanGlobals();
                    return false;
                }

                if (maxExpandedNodeCostPlusH < currentNode.totalCost + currentNode.h)
                {
                    maxExpandedNodeCostPlusH = currentNode.totalCost + currentNode.h;
                    if (debug)
                        Debug.Print("New max F: {0}", maxExpandedNodeCostPlusH);
                }
                
                // Expand
                bool wasUnexpandedNode = (currentNode.agentAExpansion == CbsNode.ExpansionState.NOT_EXPANDED &&
                                         currentNode.agentBExpansion == CbsNode.ExpansionState.NOT_EXPANDED);
                Expand(currentNode);
                if (wasUnexpandedNode)
                    highLevelExpanded++;
                // Consider moving the following into Expand()
                if (currentNode.agentAExpansion == CbsNode.ExpansionState.EXPANDED &&
                    currentNode.agentBExpansion == CbsNode.ExpansionState.EXPANDED) // Fully expanded
                    currentNode.Clear();
            }

            this.totalCost = Constants.NO_SOLUTION_COST;
            this.Clear(); // unsolvable problem - we're not going to resume it
            this.CleanGlobals();
            return false;
        }

        protected virtual bool ShouldMerge(CbsNode node)
        {
            return node.ShouldMerge(mergeThreshold);
        }

        /// <summary>
        /// TODO: Count the shuffles.
        /// TODO: Consider choosing a different conflict to work on if that current one couldn't be solved with a shuffle.
        ///       It isn't so easy, because just choosing another conflict won't work - a shuffle that changed nothing would revert to the old conflict automatically.
        /// </summary>
        /// <param name="currentNode"></param>
        protected void Shuffle(CbsNode currentNode)
        {
            if (currentNode.agentAExpansion != CbsNode.ExpansionState.NOT_EXPANDED || currentNode.agentBExpansion != CbsNode.ExpansionState.NOT_EXPANDED)
                return; // Partially expanded node was already shuffled before

            bool conflictChanged = false;
            int lastReshuffledAgent = -1; // To quiet the compiler
            int beforeLastReshuffledAgent = -1; // To quiet the compiler

            while (currentNode.GoalTest() == false)
            {
                CbsConflict conflict;
                ISet<int> group;
                int groupRepresentative;
                int internalConflictCountBefore;
                int externalConflictCountBefore;
                bool success;
                int internalConflictCountAfter;
                int externalConflictCountAfter;
                bool agentAPlanChanged = false;
                CbsConflict lastConflict = currentNode.GetConflict();

                conflict = currentNode.GetConflict();
                if (
                    ( // This is the root, and we're just starting to work on it
                     conflictChanged == false &&
                     currentNode.prev == null
                    ) ||
                    ( // Check that the first agent in the conflict wasn't just replanned or merged - shuffling in that case would necessarily have the same result since the low level is deterministic
                     conflictChanged == false &&
                     currentNode.prev != null &&
                     (
                      (currentNode.constraint == null && // This node is the result of a merge (since it isn't the root)
                       currentNode.prev.GetGroup(currentNode.prev.GetConflict().agentAIndex).Contains(conflict.agentAIndex) == false &&
                       currentNode.prev.GetGroup(currentNode.prev.GetConflict().agentBIndex).Contains(conflict.agentAIndex) == false // Agent A in this node's conflict wasn't just merged
                      ) || 
                      (currentNode.constraint != null && // This node is the result of a replan
                       conflict.agentAIndex != currentNode.agentNumToIndex[currentNode.constraint.agentNum] // Agent A wasn't just replanned
                      )
                     )
                    ) ||
                    ( // Check that the first agent in the conflict wasn't just reshuffled
                     conflictChanged == true &&
                     lastReshuffledAgent != conflict.agentAIndex
                    )
                   )
                {
                    // Are infinite improvement loops possible (a1 improves by screwing a2 and vice versa)? I don't think so, because we're looking for strictly better solutions.
                    if (debug)
                    {
                        Debug.WriteLine("");
                        Debug.WriteLine("Shuffling the first agent...");
                    }

                    groupRepresentative = currentNode.agentsGroupAssignment[conflict.agentAIndex];
                    internalConflictCountBefore = currentNode.countsOfInternalAgentsThatConflict[groupRepresentative];
                    externalConflictCountBefore = currentNode.totalExternalAgentsThatConflict;
                    SinglePlan agentAPlanBefore = currentNode.allSingleAgentPlans[conflict.agentAIndex];
                    int minCost = -1;
                    //if (this.useMddHeuristic)
                    //    minCost = currentNode.GetGroupCost(conflict.agentAIndex); // Conserve the cost. The MDD variant forces the cost to be higher than it must be according to the constraints only.
                    success = currentNode.Replan(conflict.agentAIndex, this.minDepth, null, -1, minCost);
                    if (success == false) // Usually means a timeout occured
                        break;
                    beforeLastReshuffledAgent = lastReshuffledAgent;
                    lastReshuffledAgent = conflict.agentAIndex;
                    internalConflictCountAfter = currentNode.countsOfInternalAgentsThatConflict[groupRepresentative];
                    externalConflictCountAfter = currentNode.totalExternalAgentsThatConflict;

                    if ((internalConflictCountAfter > internalConflictCountBefore) ||
                        (internalConflictCountAfter == internalConflictCountBefore) && (externalConflictCountAfter >= externalConflictCountBefore))
                    {
                        if (debug)
                            Debug.WriteLine("Shuffling the first agent didn't help: internal before=" + internalConflictCountBefore + " internal after=" + internalConflictCountAfter +
                                            " external before=" + externalConflictCountBefore + " external after=" + externalConflictCountAfter);
                        // Notice the shuffle can't increase the number of conflicts as the low level tries to minimize it,
                        // and the current configuration of paths is possible.
                    }
                    else
                    {
                        if (debug)
                        {
                            Debug.WriteLine("Shuffling the left agent helped! internal before=" + internalConflictCountBefore + " internal after=" + internalConflictCountAfter +
                                            " external before=" + externalConflictCountBefore + " external after=" + externalConflictCountAfter);
                            currentNode.Print();
                        }
                    }
                    if (currentNode.GetConflict() == null ||
                        currentNode.GetConflict().Equals(lastConflict) == false) // Shuffling can help even without changing the conflict, by resolving unselected conflicts
                    {
                        if (debug)
                        {
                            Debug.WriteLine("Conflict changed - restarting shuffle");
                            //currentNode.Print();
                        }
                        conflictChanged = true;
                        continue;
                    }

                    agentAPlanChanged = agentAPlanBefore == currentNode.allSingleAgentPlans[conflict.agentAIndex]; // Can also happen if there was no improvement
                }
                else
                {
                    if (debug)
                        Debug.WriteLine("Skipping shuffling of first agent - it was just replanned, merged or reshuffled");
                }



                if (
                    agentAPlanChanged == true || // Then even if agentB was replanned/merged/shuffled recently, agent B is going to see a new CAT
                    ( // This is the root, and we're just starting to work on it
                     conflictChanged == false &&
                     currentNode.prev == null
                    ) ||
                    ( // Check that the first agent in the conflict wasn't just replanned or merged - shuffling in that case would necessarily have the same result since the low level is deterministic
                     conflictChanged == false &&
                     currentNode.prev != null &&
                     (
                      (currentNode.constraint == null && // This node is the result of a merge (since it isn't the root)
                       currentNode.prev.GetGroup(currentNode.prev.GetConflict().agentAIndex).Contains(conflict.agentBIndex) == false &&
                       currentNode.prev.GetGroup(currentNode.prev.GetConflict().agentBIndex).Contains(conflict.agentBIndex) == false // Agent A in this node's conflict wasn't just merged
                      ) ||
                      (currentNode.constraint != null && // This node is the result of a replan
                       conflict.agentBIndex != currentNode.agentNumToIndex[currentNode.constraint.agentNum] // Agent B wasn't just replanned
                      )
                     )
                    ) ||
                    ( // Check that the first agent in the conflict wasn't just reshuffled
                     conflictChanged == true &&
                     beforeLastReshuffledAgent != conflict.agentBIndex
                    )
                   )
                {
                    if (debug)
                    {
                        Debug.WriteLine("");
                        Debug.WriteLine("Shuffling the second agent...");
                    }

                    conflict = currentNode.GetConflict();
                    groupRepresentative = currentNode.agentsGroupAssignment[conflict.agentBIndex];
                    internalConflictCountBefore = currentNode.countsOfInternalAgentsThatConflict[groupRepresentative];
                    externalConflictCountBefore = currentNode.totalExternalAgentsThatConflict;
                    int minCost = -1;
                    //if (this.useMddHeuristic)
                    //    minCost = currentNode.GetGroupCost(conflict.agentBIndex); // Conserve the cost. The MDD variant forces the cost to be higher than it must be according to the constraints only.
                    success = currentNode.Replan(conflict.agentBIndex, this.minDepth, null, -1, minCost);
                    if (success == false) // Usually means a timeout occured
                        break;
                    beforeLastReshuffledAgent = lastReshuffledAgent;
                    lastReshuffledAgent = conflict.agentBIndex;
                    internalConflictCountAfter = currentNode.countsOfInternalAgentsThatConflict[groupRepresentative];
                    externalConflictCountAfter = currentNode.totalExternalAgentsThatConflict;

                    if ((internalConflictCountAfter > internalConflictCountBefore) ||
                        (internalConflictCountAfter == internalConflictCountBefore) && (externalConflictCountAfter >= externalConflictCountBefore))
                    {
                        if (debug)
                            Debug.WriteLine("Shuffling the second agent didn't help: internal before=" + internalConflictCountBefore + " internal after=" + internalConflictCountAfter +
                                            " external before=" + externalConflictCountBefore + " external after=" + externalConflictCountAfter);
                    }
                    else
                    {
                        if (debug)
                        {
                            Debug.WriteLine("Shuffling the second agent helped! internal before=" + internalConflictCountBefore + " internal after=" + internalConflictCountAfter +
                                            " external before=" + externalConflictCountBefore + " external after=" + externalConflictCountAfter);
                            currentNode.Print();
                        }
                    }

                }
                else
                {
                    if (debug)
                        Debug.WriteLine("Skipping shuffling of second agent - it was just replanned, merged or reshuffled and the first agent's shuffle didn't change the first agent's plan"); // So the second agent will see the exact same CAT
                }

                if (currentNode.GetConflict() == null ||
                    currentNode.GetConflict().Equals(lastConflict) == false)
                {
                    if (debug)
                    {
                        Debug.WriteLine("Conflict changed - restarting shuffle");
                        //currentNode.Print();
                    }
                    conflictChanged = true;
                    continue;
                }
                else
                {
                    if (debug)
                        Debug.WriteLine("Still the exact same conflict - stop shuffling");
                    break;
                }
            }
        }

        public virtual void Reset()
        {
            this.restarts++;
            this.openList.Clear();
            this.closedList.Clear();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="node"></param>
        /// <param name="node"></param>
        /// <param name="children"></param>
        /// <param name="adoptBy">If not given, adoption is done by expanded node</param>
        /// <returns>true if adopted - need to rerun this method, ignoring the returned children from this call, bacause adoption was performed</returns>
        protected bool ExpandImpl(CbsNode node, bool adopt, out IList<CbsNode> children, out bool reinsertParent, CbsNode adoptBy = null)
        {
            CbsConflict conflict = node.GetConflict();
            children = new List<CbsNode>();
            if (adoptBy == null)
                adoptBy = node;
            ushort adoptByH = adoptBy.h;

            CbsNode child;
            reinsertParent = false;
            int closedListHitChildCost;
            bool leftSameCost = false; // To quiet the compiler
            bool rightSameCost = false;

            if (this.mergeThreshold != -1 && ShouldMerge(node))
            {
                if (this.mergeCausesRestart == false)
                {
                    child = this.MergeExpand(node, out closedListHitChildCost);
                    if (child == null)
                        return false; // A timeout occured, or the child was already in the closed list, or there were just too many constraints (happens with ID, which adds whole paths as constraints)
                }
                else
                {
                    // TODO: What if planning a path for the merged agents finds a path with the same
                    // cost as the sum of their current paths and no other conflicts exist
                    child = new CbsNode(this.instance.m_vAgents.Length, this.solver, this.singleAgentSolver, this, node.agentsGroupAssignment);
                    child.MergeGroups(node.agentsGroupAssignment[conflict.agentAIndex], node.agentsGroupAssignment[conflict.agentBIndex],
                        false);
                    this.maxSizeGroup = Math.Max(this.maxSizeGroup, child.GetGroupSize(conflict.agentAIndex));
                    bool solved = child.Solve(this.minDepth);
                    if (solved == false)
                        return false;
                    //if ((child.allSingleAgentCosts[child.agentsGroupAssignment[conflict.agentAIndex]] ==
                    //    node.allSingleAgentCosts[node.agentsGroupAssignment[conflict.agentAIndex]] +
                    //    node.allSingleAgentCosts[node.agentsGroupAssignment[conflict.agentBIndex]]) &&
                    //    //All of node's conflicts are between the agents of the merged groups)
                    if (this.debug)
                        Debug.WriteLine("Restarting the search with merged agents.");
                    this.Reset();
                }
                // No need to try to adopt the child - there's only one so we're not branching.
                children.Add(child);
                return false;
            }

            // Generate left child:
            if (node.conflict.timeStepAgentA != 0)
            {
                child = ConstraintExpand(node, true, out closedListHitChildCost);
                if (child != null)
                {
                    if (child == node) // Expansion deferred
                        reinsertParent = true;
                    else // New child
                    {
                        // First fit adoption: Greedily adopt the first child that's better than the parent
                        if (adopt &&
                            AdoptConditionally(adoptBy, child, adoptByH))
                            return true;
                        children.Add(child);
                        leftSameCost = child.totalCost == node.totalCost;
                    }
                }
                else  // A timeout occured, or the child was already in the closed list.
                {
                    if (closedListHitChildCost != -1)
                        leftSameCost = closedListHitChildCost == node.totalCost;
                }

                if (runner.ElapsedMilliseconds() > Constants.MAX_TIME)
                    return false;
            }
            // Generate right child:
            if (node.conflict.timeStepAgentB != 0)
            {
                child = ConstraintExpand(node, false, out closedListHitChildCost);
                if (child != null)
                {
                    if (child == node) // Expansion deferred
                        reinsertParent = true;
                    else // New child
                    {
                        // First fit adoption: Greedily adopt the first child that's better than the parent
                        if (adopt &&
                            AdoptConditionally(adoptBy, child, adoptByH))
                            return true;
                        children.Add(child);
                        rightSameCost = child.totalCost == node.totalCost;
                    }
                }
                else  // A timeout occured, or the child was already in the closed list.
                {
                    if (closedListHitChildCost != -1)
                        rightSameCost = closedListHitChildCost == node.totalCost;
                }
            }
            if (node.agentAExpansion == CbsNode.ExpansionState.DEFERRED || node.agentBExpansion == CbsNode.ExpansionState.DEFERRED)
                this.semiCardinalConflictSplits++; // Count only on the first expansion of the node. On the second expansion no count will be incremented.
            else
            {
                if (leftSameCost && rightSameCost)
                    this.nonCardinalConflictSplits++;
                else if (leftSameCost || rightSameCost)
                    this.semiCardinalConflictSplits++;
                else
                    this.cardinalConflictSplits++;
            }

            return false;
        }

        public virtual void Expand(CbsNode node)
        {
            ushort parentCost = node.totalCost;
            ushort parentH = node.h;
            IList<CbsNode> children = null; // To quiet the compiler
            bool reinsertParent = false; // To quiet the compiler

            int origCardinalConflictSplits = this.cardinalConflictSplits;
            int origSemiCardinalConflictSplits = this.semiCardinalConflictSplits;
            int origNonCardinalConflictSplits = this.nonCardinalConflictSplits;

            //var costs = new CostTreeNode(node.allSingleAgentCosts);
            //bool alreadyLookedAhead = this.lookedAhead.Contains(costs);

            if (debug)
                if (this.bypassStrategy == BypassStrategy.BEST_FIT_LOOKAHEAD && node.parentAlreadyLookedAheadOf)
                    Debug.Print("Not looking ahead from this node, one of its ancestors of the same cost was already looked ahead of");

            if (this.conflictChoice == ConflictChoice.FIRST || this.conflictChoice == ConflictChoice.MOST_CONFLICTING) // They just a conflict once and stick with it.
            {
                if (this.bypassStrategy == BypassStrategy.NONE)
                {
                    this.ExpandImpl(node, false, out children, out reinsertParent, null);
                }
                else if (this.bypassStrategy == BypassStrategy.FIRST_FIT_LOOKAHEAD || node.parentAlreadyLookedAheadOf) // Do bypass but don't look ahead
                {
                    bool adoptionPerformedBefore = false;
                    while (true) // Until a node with a higher cost is found or a goal is found
                    {
                        var lookAheadOpenList = new OpenList(this);
                        var lookAheadSameCostNodes = new HashSet<CbsNode>();
                        var lookAheadLargerCostNodes = new HashSet<CbsNode>();
                        var lookAheadSameCostNodesToReinsertWithHigherCost = new HashSet<CbsNode>();
                        lookAheadOpenList.Add(node);

                        if (debug)
                        {
                            if (lookAheadOpenList.Count != 0)
                                Debug.Print("Starting lookahead:");
                        }
                        IList<CbsNode> lookAheadChildren;
                        bool lookAheadReinsertParent;
                        bool adopted = false;
                        int expansions = 0;
                        while (lookAheadOpenList.Count != 0)
                        {
                            if (expansions + 1 > this.lookaheadMaxExpansions) // + 1 because we're checking before the coming expansion. lookaheadMaxExpansions = 1 is the minimum working value.
                            {
                                if (debug)
                                    Debug.WriteLine("Lookahead count exceeded. Stopping lookahead.");
                                break;
                            }

                            if (runner.ElapsedMilliseconds() > Constants.MAX_TIME)
                                return;

                            CbsNode lookAheadNode = (CbsNode)lookAheadOpenList.Remove();
                            lookAheadNode.ChooseConflict();

                            if (debug)
                                Debug.WriteLine("Looking ahead from node hash: " + lookAheadNode.GetHashCode() + ".");

                            adopted = this.ExpandImpl(lookAheadNode, true, out lookAheadChildren, out lookAheadReinsertParent, node);
                            expansions++;

                            if (adopted == true)
                            {
                                this.lookAheadNodesCreated += lookAheadChildren.Count + 1; // Either 1, if the first child was adopted, or 2 otherwise. +1 for the node itself before it adopted another solution.
                                this.cardinalConflictSplits = origCardinalConflictSplits;
                                this.semiCardinalConflictSplits = origSemiCardinalConflictSplits;
                                this.nonCardinalConflictSplits = origNonCardinalConflictSplits;
                                break; // Insert children into open list
                            }

                            if (lookAheadReinsertParent)
                                lookAheadSameCostNodesToReinsertWithHigherCost.Add(lookAheadNode);

                            foreach (var child in lookAheadChildren)
                            {
                                this.lookAheadNodesCreated++;
                                this.closedList.Add(child, child); // Temporarily! Just so look ahead expansion can use this data
                                if (child.totalCost == node.totalCost)
                                {
                                    lookAheadOpenList.Add(child);
                                    lookAheadSameCostNodes.Add(child);
                                }
                                else
                                    lookAheadLargerCostNodes.Add(child); // No need to check if it's already there since the closed list is properly maintained
                            }
                        }

                        if (debug)
                            if (adopted == false && lookAheadOpenList.Count == 0)
                                Debug.WriteLine("Lookahead exhausted all same cost nodes.");

                        if (adopted && adoptionPerformedBefore == false)
                        {
                            adoptionPerformedBefore = true;
                            this.bypasses++; // Only count one adoption for the entire look ahead subtree branch traversed.
                        }

                        if (node.GoalTest()) // Lookahead found an admissable solution! (The original node to expand wasn't a goal)
                        {
                            if (debug)
                                Debug.WriteLine("Goal found with same cost - stopping lookahead.");
                            this.openList.Add(node);
                            return;
                        }

                        if (adopted == false) // Insert children into open list
                        {
                            if (node.h < 1 && lookAheadOpenList.Count == 0) // Looked ahead exhaustively
                                node.h = 1; // We know the goal isn't under it with the same cost

                            // We already expanded this whole subtree of same cost nodes (at least partially). Insert the frontier to the open list.
                            reinsertParent = false; // It may reinserted as an element of the lookAheadSameCostNodesToReinsertWithHigherCost
                            children = lookAheadLargerCostNodes.ToList<CbsNode>(); // Larger cost nodes aren't expanded so they're always on the frontier
                            int unexpandedSameCostNodes = lookAheadOpenList.Count;
                            while (lookAheadOpenList.Count != 0)
                            {
                                var child = (CbsNode)lookAheadOpenList.Remove();
                                children.Add(child); // Unexapnded so it's also on the frontier
                                this.closedList.Remove(child); // Just so it'll be inserted into the open list at the end of the method
                            }
                            foreach (var reInsertNode in lookAheadSameCostNodesToReinsertWithHigherCost)
                            {
                                if (reInsertNode == node)
                                {
                                    reinsertParent = true;
                                    continue;
                                }
                                children.Add(reInsertNode);
                                this.closedList.Remove(reInsertNode); // Just so it'll be inserted into the open list at the end of the method
                                //if (reInsertNode == node) // Re-inserting the original node that was to be expanded, with a higher h
                                //    this.highLevelGenerated--; // It'll be counted as a new generated child later, and we don't need to count it twice
                            }
                            foreach (CbsNode lookAheadNode in lookAheadLargerCostNodes)
                                this.closedList.Remove(lookAheadNode); // Just so they'll be inserted into the open list at the end of the method
                            this.lookAheadNodesCreated -= lookAheadSameCostNodes.Count + lookAheadLargerCostNodes.Count; // None of these nodes weren't wasted effort, they were properly generated
                            this.highLevelGenerated += lookAheadSameCostNodes.Count
                                                       - lookAheadSameCostNodesToReinsertWithHigherCost.Count
                                                       - unexpandedSameCostNodes; // You could say they were generated and not looked ahead at, and they won't be counted later.
                            this.highLevelExpanded += lookAheadSameCostNodes.Count - unexpandedSameCostNodes;

                            break;
                        }
                        else // Adopted. Do a new round of expansions.
                        {
                            foreach (CbsNode lookAheadNode in lookAheadLargerCostNodes)
                                this.closedList.Remove(lookAheadNode); // Just so they'll be inserted into the open list at the end of the method
                            foreach (CbsNode lookAheadNode in lookAheadSameCostNodes)
                                this.closedList.Remove(lookAheadNode); // Just so they'll be inserted into the open list at the end of the method
                            // The only difference in closed list cleanup after adoption is that expanded same cost nodes are also removed from the closed list.
                        }
                    }
                }
                else // this.bypassStrategy == BypassStrategy.BEST_FIT_LOOKAHEAD and this set of costs not already done
                {
                    // FIXME: lookaheadMaxExpansions isn't respected correctly here. We actually limit the number of same-cost nodes generated, which is similar but not the same.
                    var lookAheadOpenList = new OpenList(this);
                    var lookAheadSameCostNodes = new HashSet<CbsNode>();
                    var lookAheadLargerCostNodes = new HashSet<CbsNode>();
                    var lookAheadSameCostNodesToReinsertWithHigherCost = new HashSet<CbsNode>();
                    lookAheadOpenList.Add(node);
                    node.parentAlreadyLookedAheadOf = true;

                    if (debug)
                    {
                        if (lookAheadOpenList.Count != 0)
                            Debug.Print("Starting lookahead:");
                    }
                    IList<CbsNode> lookAheadChildren;
                    bool lookAheadReinsertParent;
                    while (lookAheadOpenList.Count != 0)
                    {
                        if (lookAheadSameCostNodes.Count + 1 >= this.lookaheadMaxExpansions) // + 1 for the root
                        {
                            if (debug)
                                Debug.WriteLine("Lookahead count exceeded. Stopping lookahead.");
                            break;
                        }

                        CbsNode lookAheadNode = (CbsNode)lookAheadOpenList.Remove();
                        lookAheadNode.ChooseConflict();

                        if (runner.ElapsedMilliseconds() > Constants.MAX_TIME)
                            return;

                        if (debug)
                            Debug.WriteLine("Looking ahead from node hash: " + lookAheadNode.GetHashCode() + ".");

                        this.ExpandImpl(lookAheadNode, false, out lookAheadChildren, out lookAheadReinsertParent); // Ignoring return val since we're explicitly not allowing adoption

                        if (lookAheadReinsertParent)
                            lookAheadSameCostNodesToReinsertWithHigherCost.Add(lookAheadNode);

                        foreach (var child in lookAheadChildren)
                        {
                            this.lookAheadNodesCreated++;
                            this.closedList.Add(child, child); // Temporarily! Just so look ahead expansion can use this data
                            if (child.totalCost == node.totalCost)
                            {
                                if (child.GoalTest()) // Lookahead found an admissable solution!
                                {
                                    if (debug)
                                        Debug.WriteLine("Goal found with same cost - stopping lookahead.");
                                    this.openList.Add(child); // Technically should have just breaked and let node adopt child. This is just a short-cut.
                                    this.bypasses++; // Just a technicality needed to make the adoption count not lower than if we didn't use immediate adoption. You could say we adopt the goal's solution.
                                    return;
                                }

                                if (lookAheadSameCostNodes.Contains(child) == false)
                                {
                                    lookAheadOpenList.Add(child);
                                    lookAheadSameCostNodes.Add(child);
                                }
                            }
                            else
                                lookAheadLargerCostNodes.Add(child); // No need to check if it's already there :)
                        }
                    }

                    if (node.h < 1 && lookAheadOpenList.Count == 0) // Looked ahead exhaustively
                        node.h = 1; // We know the goal isn't under it with the same cost

                    bool bypassPerformed = false;
                    // Find the best look ahead node and check if it's worth adopting
                    if (lookAheadSameCostNodes.Count != 0)
                    {
                        IList<CbsNode> lookAheadSameCostNodesSerialzed = lookAheadSameCostNodes.ToList<CbsNode>();
                        CbsNode adoptionCandidate = lookAheadSameCostNodesSerialzed[0];
                        for (int i = 1; i < lookAheadSameCostNodesSerialzed.Count; i++)
                        {
                            if (lookAheadSameCostNodesSerialzed[i].CompareToIgnoreH(adoptionCandidate) == -1)
                                adoptionCandidate = lookAheadSameCostNodesSerialzed[i];
                        }
                        if (AdoptConditionally(node, adoptionCandidate, parentH))
                        {
                            bypassPerformed = true;
                            this.bypasses++;

                            this.cardinalConflictSplits = origCardinalConflictSplits;
                            this.semiCardinalConflictSplits = origSemiCardinalConflictSplits;
                            this.nonCardinalConflictSplits = origNonCardinalConflictSplits;

                            // Remove cancelled look-ahead nodes from closed list
                            foreach (CbsNode lookAheadNode in lookAheadSameCostNodes)
                                this.closedList.Remove(lookAheadNode);
                            foreach (CbsNode lookAheadNode in lookAheadLargerCostNodes)
                                this.closedList.Remove(lookAheadNode);

                            if (this.openList.Count != 0 && ((CbsNode)this.openList.Peek()).f < node.f)
                            // This is a new unexpanded node, and we just raised its h, so we can push it back
                            // FIXME: More duplication of the push back logic
                            {
                                reinsertParent = true;
                                children = new List<CbsNode>(); // Children will be generated when this node comes out of the open list

                                if (this.debug)
                                    Debug.Print("Reinserting node into the open list with h=1, since the goal wasn't found with the same cost under it.");
                            }
                            else
                            {
                                // We updated the node, need to re-expand it. Surprisingly, re-expansion can produce even better nodes for adoption, so we need to allow immediate expansion too.
                                // TODO: Just re-run the infinite lookahead? That would be the immediate adoption variant above
                                this.Expand(node);
                                return;
                            }
                        }
                    }

                    if (bypassPerformed == false)
                    {
                        // Adoption not performed, and we already expanded this whole subtree of same cost nodes (at least partially)
                        reinsertParent = false; // It may reinserted as an element of the children list
                        children = lookAheadLargerCostNodes.ToList<CbsNode>();
                        int unexpandedSameCostNodes = lookAheadOpenList.Count;
                        while (lookAheadOpenList.Count != 0)
                        {
                            var child = (CbsNode)lookAheadOpenList.Remove();
                            children.Add(child);
                            this.closedList.Remove(child); // Just so it'll be inserted into the open list at the end of the method
                        }
                        foreach (var reInsertNode in lookAheadSameCostNodesToReinsertWithHigherCost)
                        {
                            if (reInsertNode == node)
                            {
                                reinsertParent = true;
                                continue;
                            }
                            children.Add(reInsertNode);
                            this.closedList.Remove(reInsertNode); // Just so it'll be inserted into the open list at the end of the method
                            if (reInsertNode == node) // Re-inserting the original node that was to be expanded, with a higher h
                                this.highLevelGenerated--; // It'll be counted as a new generated child later, and we don't need to count it twice
                        }
                        foreach (CbsNode lookAheadNode in lookAheadLargerCostNodes)
                            this.closedList.Remove(lookAheadNode); // Just so they'll be inserted into the open list at the end of the method
                        this.lookAheadNodesCreated -= lookAheadSameCostNodes.Count + lookAheadLargerCostNodes.Count; // These nodes weren't wasted effort
                        this.highLevelGenerated += lookAheadSameCostNodes.Count - lookAheadSameCostNodesToReinsertWithHigherCost.Count - unexpandedSameCostNodes; // You could say they were generated and not looked ahead at, and they won't be counted later.
                        this.highLevelExpanded += lookAheadSameCostNodes.Count - unexpandedSameCostNodes;
                    }
                }


                // Both children considered. None adopted. Add them to the open list, and re-insert the partially expanded parent too if necessary.
                if (reinsertParent)
                    this.openList.Add(node); // Re-insert node into open list with higher cost, don't re-increment global conflict counts

                foreach (var child in children)
                {
                    closedList.Add(child, child);

                    if (child.totalCost == parentCost) // Total cost didn't increase (yet)
                    {
                        child.h = parentH;

                        if (this.bypassStrategy == BypassStrategy.BEST_FIT_LOOKAHEAD)
                            child.parentAlreadyLookedAheadOf = true;
                    }
                    else if (this.bypassStrategy == BypassStrategy.BEST_FIT_LOOKAHEAD)
                       child.parentAlreadyLookedAheadOf = false;

                    if (child.totalCost <= this.maxCost)
                    {
                        this.highLevelGenerated++;
                        openList.Add(child);
                    }
                }
            }
            else // We try to split according to cardinal conflicts
            {
                Debug.Assert(this.bypassStrategy == BypassStrategy.NONE || this.lookaheadMaxExpansions == 1, "Only BP1 is supported with cardinal conflict choice");  // Assumed in order to simplify code
                Debug.Assert(this.bypassStrategy != BypassStrategy.BEST_FIT_LOOKAHEAD, "Only first-fit adoption is supported with cardinal conflict choice"); // Assumed in order to simplify code
                HashSet<CbsConflict> conflictsProcessed = new HashSet<CbsConflict>(); // Needed if we restart iteration because of adopting a bypass

                bool adoptionPerformedBefore = false;

                while (true)
                {
                    conflictsProcessed.Add(node.conflict);

                    bool adopted = this.ExpandImpl(node, bypassStrategy != BypassStrategy.NONE, out children, out reinsertParent, null);

                    if (this.mergeCausesRestart && (this.closedList.Count == 0)) // HACK: Means a restart was triggered
                        break;

                    if (adopted)
                    {
                        if (adoptionPerformedBefore == false)
                        {
                            adoptionPerformedBefore = true;
                            this.bypasses++; // Only count one adoption for the entire look ahead subtree branch traversed.
                        }
                        this.lookAheadNodesCreated += children.Count + 1; // Either 1, if the first child was adopted, or 2 otherwise. +1 for the node itself before it adopted another solution.
                        this.cardinalConflictSplits = origCardinalConflictSplits;
                        this.semiCardinalConflictSplits = origSemiCardinalConflictSplits;
                        this.nonCardinalConflictSplits = origNonCardinalConflictSplits;
                        
                        if (node.GoalTest()) // Adoption found an admissable solution! (The original node to expand wasn't a goal)
                        {
                            if (debug)
                                Debug.WriteLine("Bypass found a goal! Inserting it into OPEN.");
                            this.openList.Add(node);
                            return;
                        }
                        
                        node.conflict = null; // Probably already is null, since it adopted the conflict from its child and its child hasn't chosen yet. Just to be safe.
                                              // This is needed to make ChooseConflict() do anything.
                        node.ChooseConflict(); // There must be a conflict, since this isn't a goal
                        while (conflictsProcessed.Contains(node.conflict))
                        {
                            bool choseNext = node.ChooseNextConflict();
                            if (choseNext == false)
                                break; // No other conflict available, split according to current one, even though it was already checked - it isn't a cardinal conflict.
                        }
                        continue;
                    }

                    if (
                        children.All(child => child.totalCost > node.totalCost) && // All generated nodes have a higher cost
                        ((node.agentAExpansion == CbsNode.ExpansionState.EXPANDED && node.agentBExpansion == CbsNode.ExpansionState.EXPANDED) || // Both children generated by now
                        (node.agentAExpansion == CbsNode.ExpansionState.EXPANDED && node.agentBExpansion == CbsNode.ExpansionState.DEFERRED) || // One child still deferred, will surely have a higher cost (that's why it was deferred)
                        (node.agentAExpansion == CbsNode.ExpansionState.DEFERRED && node.agentBExpansion == CbsNode.ExpansionState.EXPANDED)) // One child still deferred, will surely have a higher cost (that's why it was deferred)
                            ) // A cardinal conflict
                    {
                        if (this.debug)
                            Debug.WriteLine("This was a cardinal conflict (or at least a deferred child). Inserting the generated children into OPEN.");

                        break; // Look no further
                    }

                    if (children.Any(child => child.GoalTest() && child.totalCost == node.totalCost)) // Admissable goal found (and adoption isn't enabled, otherwise the goal would have been adopted above)
                    {
                        if (this.debug)
                            Debug.WriteLine("Admissable goal found! Inserting the generated children into OPEN.");

                        break; // Look no further
                    }

                    bool rerunExpansion = false;
                    CbsConflict originalConflict = node.conflict;
                    while (node.nextConflictCouldBeCardinal && node.ChooseNextConflict())
                    {
                        if (conflictsProcessed.Contains(node.conflict) == false) // We didn't already work on this conflict yet.
                        {
                            rerunExpansion = true;
                            break;
                        }
                        else
                        {
                            if (this.debug)
                                Debug.WriteLine("Skipping conflict " + node.conflict.ToString() + " - we already know it isn't cardinal");
                        }
                    }
                    if (rerunExpansion == false)
                    {
                        node.conflict = originalConflict; // May have been cycled by the above loop but re-expanding was skipped as the new conflict was already tried.
                                                          // Restoring the conflict is important in order to have a consistent tree where constraints come from the conflict
                                                          // of the node's parent
                        if (this.debug)
                            Debug.WriteLine("This was a non-cardinal conflict but there aren't more promising conflicts to try. Inserting the generated children into OPEN.");
                        break;
                    }

                    if (this.debug)
                        Debug.WriteLine("This was a non-cardinal conflict. Trying another conflict: " + node.conflict.ToString());
                    // Cancel partial expansion effects:
                    node.agentAExpansion = CbsNode.ExpansionState.NOT_EXPANDED;
                    node.agentBExpansion = CbsNode.ExpansionState.NOT_EXPANDED;
                    node.h = parentH;
                    // Take care of counts:
                    this.lookAheadNodesCreated += children.Count; // FIXME: This is a different kind of lookahead! Not looking at 
                                                                  // nodes in the subtree below the node to expand that have the
                                                                  // same cost and trying to find one with less conflicts,
                                                                  // this time, it's looking ahead at the immediate children
                                                                  // of a node to check if the conflict is cardinal.
                }

                if (reinsertParent)
                    this.openList.Add(node); // Re-insert node into open list with higher cost, don't re-increment global conflict counts
                else
                    node.ClearConflictChoiceData(); // We could do it even if there was a partial expansion and the node is
                                                    // being re-inserted into OPEN.
                                                    // If a lookahead chooser settled on
                                                    // a semi-cardinal or non-cardinal, then there are no other conflicts left
                                                    // so the conflict choosing state is unnecessary, but then ChooseNextConflict
                                                    // would need to allow the iterator to be null.
                                                    // If an MDD building chooser settled on a semi-cardinal or non-cardinal,
                                                    // there could still be other conflicts, but they are known not to be cardinal.
                foreach (var child in children)
                {
                    closedList.Add(child, child);

                    if (child.totalCost == parentCost) // Total cost didn't increase
                        child.h = parentH;

                    if (child.totalCost <= this.maxCost)
                    {
                        this.highLevelGenerated++;
                        openList.Add(child);
                    }
                }
            }
        }

        /// <summary>
        /// Returns whether the node is solvable with the current costs. If unsure, returns true;
        /// Currently avoiding building a k-agent MDD.
        /// </summary>
        /// <param name="node"></param>
        /// <returns>Whether MDD after pruning believes that the node's configuration of costs is solvable.</returns>
        protected bool MDDHeuristic(CbsNode node)
        {
            var costsNode = new CostTreeNode(node.allSingleAgentCosts);
            if (this.costTreeMDDResults.ContainsKey(costsNode)) // TODO: What if it was computed on a different pair of agents and returned 0? We could try again then.
            {
                return this.costTreeMDDResults[costsNode];
            }

            if (node.GoalTest())
            {
                this.costTreeMDDResults.Add(costsNode, true);
                return true;
            }

            if (node.GetGroupSize(node.conflict.agentAIndex) > 1 || node.GetGroupSize(node.conflict.agentBIndex) > 1)
            {
                return true; // Without saving the result, as it's just a cop-out
            }

            /*
            var nodeSolver = new CostTreeNodeSolverOldMatching(this.instance, costsNode, this.runner, 2);
            //SinglePlan[] plans = nodeSolver.Solve(null, null);
            //if (plans == null)
            if (nodeSolver.Prune())
             */
            int maxCost = Math.Max(node.allSingleAgentCosts[node.conflict.agentAIndex],
                                   node.allSingleAgentCosts[node.conflict.agentBIndex]);
            var mddA = new MDD(node.conflict.agentAIndex, this.instance.m_vAgents[node.conflict.agentAIndex].agent.agentNum,
                                this.instance.m_vAgents[node.conflict.agentAIndex].lastMove,
                                costsNode.costs[node.conflict.agentAIndex], maxCost,
                                this.instance.GetNumOfAgents(), this.instance, true);
            var mddB = new MDD(node.conflict.agentBIndex, this.instance.m_vAgents[node.conflict.agentBIndex].agent.agentNum,
                               this.instance.m_vAgents[node.conflict.agentBIndex].lastMove,
                               costsNode.costs[node.conflict.agentBIndex], maxCost,
                               this.instance.GetNumOfAgents(), this.instance, true);
            this.mddsBuilt += 2;
            MDD.PruningDone ans = mddA.SyncMDDs(mddB, false);
            if (ans == MDD.PruningDone.EVERYTHING)
            {
                this.costTreeMDDResults.Add(costsNode, false);
                this.pruningSuccesses++;
                return false;
            }
            else
            {
                this.costTreeMDDResults.Add(costsNode, true);
                this.pruningFailures++;
                return true;
            }
        }

        /// <summary>
        /// </summary>
        /// <param name="node"></param>
        /// <param name="closedListHitChildCost"></param>
        /// <returns>null if planning the child's path failed, otherwise returns the new child</returns>
        protected CbsNode MergeExpand(CbsNode node, out int closedListHitChildCost)
        {
            CbsConflict conflict = node.GetConflict();
            closedListHitChildCost = -1;
            
            CbsNode child = new CbsNode(node, node.agentsGroupAssignment[conflict.agentAIndex], node.agentsGroupAssignment[conflict.agentBIndex]);
            if (closedList.ContainsKey(child) == false) // We may have already merged these agents in the parent
            {
                if (debug)
                    Debug.WriteLine("Merging agents {0} and {1}", conflict.agentAIndex, conflict.agentBIndex);
                bool success = child.Replan(conflict.agentAIndex, this.minDepth); // or agentBIndex. Doesn't matter - they're in the same group.

                if (success == false) // A timeout probably occured
                    return null;

                if (debug)
                {
                    Debug.WriteLine("Child hash: " + child.GetHashCode());
                    Debug.WriteLine("Child cost: " + child.totalCost);
                    Debug.WriteLine("Child min ops to solve: " + child.minOpsToSolve);
                    Debug.WriteLine("");
                }

                this.maxSizeGroup = Math.Max(this.maxSizeGroup, child.replanSize);

                return child;
            }
            else
            {
                closedListHits++;
                closedListHitChildCost = closedList[child].totalCost;
            }
            return null;
        }

        protected CbsNode ConstraintExpand(CbsNode node, bool doLeftChild, out int closedListHitChildCost)
        {
            CbsConflict conflict = node.GetConflict();
            int conflictingAgentIndex = doLeftChild? conflict.agentAIndex : conflict.agentBIndex;
            CbsNode.ExpansionState expansionsState = doLeftChild ? node.agentAExpansion : node.agentBExpansion;
            CbsNode.ExpansionState otherChildExpansionsState = doLeftChild ? node.agentBExpansion : node.agentAExpansion;
            string agentSide = doLeftChild? "left" : "right";
            int planSize = node.allSingleAgentPlans[conflictingAgentIndex].GetSize();
            int groupSize = node.GetGroupSize(conflictingAgentIndex);
            closedListHitChildCost = -1;

            if ((Constants.Variant == Constants.ProblemVariant.ORIG &&
                expansionsState == CbsNode.ExpansionState.NOT_EXPANDED && conflict.vertex == true &&
                 conflict.timeStep >= node.allSingleAgentCosts[conflictingAgentIndex] && // TODO: Can't just check whether the node is at its goal - the plan may involve it passing through its goal and returning to it later because of preexisting constraints.
                 node.h < conflict.timeStep + 1 - node.allSingleAgentCosts[conflictingAgentIndex] && // Otherwise we won't be increasing its h and there would be no reason to delay expansion
                 groupSize == 1) || // Otherwise an agent in the group can be forced to take a longer route without increasing the group's cost because another agent would be able to take a shorter route.
               (Constants.Variant == Constants.ProblemVariant.NEW &&
                expansionsState == CbsNode.ExpansionState.NOT_EXPANDED && conflict.vertex == true &&
                ((conflict.timeStep > planSize - 1 && node.h < 2) ||
                 (conflict.timeStep == planSize - 1 && node.h < 1)) &&
                groupSize == 1)) // Otherwise an agent in the group can be forced to take a longer route without increasing the group's cost because another agent would be able to take a shorter route.
            // Conflict happens when or after the agent reaches its goal, and the agent is in a single-agent group.
            // With multi-agent groups, banning the goal doesn't guarantee a higher cost solution,
            // since if an agent is forced to take a longer route it may enable another agent in the group
            // to take a shorter route, getting an alternative solution of the same cost
            // The child would cost a lot because:
            // A) All WAIT moves in the goal before leaving it now add to the g (if we're in the original problem variant).
            // B) We force the low level to compute a path longer than the optimal,
            //    and with a bad suprise towards the end in the form of a constraint,
            //    so the low-level's SIC heuristic performs poorly.
            // C) We're banning the GOAL from all directions (since this is a vertex conflict),
            //    so any alternative plan will at least cost 1 more.
            //    We're ignoring edge conflicts because they can only happen at the goal when reaching it,
            //    and aren't guaranteed to increase the cost because the goal can still be possibly reached from another edge.
            {
                if (otherChildExpansionsState == CbsNode.ExpansionState.DEFERRED)
                        throw new Exception("Unexpected: Expansion of both children deffered, but this is a vertex conflict so that means the targets for the two agents are equal, which is illegal");

                if (debug)
                    Debug.WriteLine("Skipping " + agentSide + " child for now");
                if (doLeftChild)
                    node.agentAExpansion = CbsNode.ExpansionState.DEFERRED;
	            else
                    node.agentBExpansion = CbsNode.ExpansionState.DEFERRED;
                // Add the minimal delta in the child's cost:
                // since we're banning the goal at conflict.timeStep, it must at least do conflict.timeStep+1 steps
                if (Constants.Variant == Constants.ProblemVariant.ORIG)
                    node.h = (ushort)(conflict.timeStep + 1 - node.allSingleAgentCosts[conflictingAgentIndex]);
                else if (Constants.Variant == Constants.ProblemVariant.NEW)
                {
                    if (conflict.timeStep > planSize - 1) // Agent will need to step out and step in to the goal, at least
                        node.h = 2;
                    else // Conflict is just when agent enters the goal, it'll have to at least wait one timestep.
                        node.h = 1;
                }
                this.partialExpansions++;
                return node;
            }
            else if (expansionsState != CbsNode.ExpansionState.EXPANDED)
            // Agent expansion already skipped in the past or not forcing it from its goal - finally generate the child:
            {
                if (debug)
                    Debug.WriteLine("Generating " + agentSide +" child");

                if (doLeftChild)
                    node.agentAExpansion = CbsNode.ExpansionState.EXPANDED;
                else
                    node.agentBExpansion = CbsNode.ExpansionState.EXPANDED;
                
                var newConstraint = new CbsConstraint(conflict, instance, doLeftChild, constraintPolicy);
                CbsNode child = new CbsNode(node, newConstraint, conflictingAgentIndex);

                if (this.doMalte && doLeftChild == false)
                {
                    // Add the case where both agents shouldn't be at the conflict point to the _left_ child
                    // by forcing the first agent in the _right_ child to _always_ be at the conflict point.
                    // Notice that vertex conflicts create must constraint with NO_DIRECTION and edge conflicts
                    // don't, as necessary.
                    var newMustConstraint = new CbsConstraint(conflict, instance, true, constraintPolicy);
                    child.SetMustConstraint(newMustConstraint);
                }

                if (closedList.ContainsKey(child) == false)
                {
                    int minCost = -1;
                    //if (this.useMddHeuristic)
                    //    minCost = node.GetGroupCost(conflictingAgentIndex) + 1;
                    bool success = child.Replan(conflictingAgentIndex, this.minDepth, null, -1, minCost); // The node takes the max between minDepth and the max time over all constraints.

                    if (success == false)
                        return null; // A timeout probably occured

                    if (debug)
                    {
                        Debug.WriteLine("Child hash: " + child.GetHashCode());
                        Debug.WriteLine("Child cost: " + child.totalCost);
                        Debug.WriteLine("Child min ops to solve: " + child.minOpsToSolve);
                        Debug.WriteLine("Child num of agents that conflict: " + child.totalInternalAgentsThatConflict);
                        Debug.WriteLine("Child num of internal conflicts: " + child.totalConflictsBetweenInternalAgents);
                        Debug.WriteLine("");
                    }

                    if (child.totalCost < node.totalCost && groupSize == 1) // Catch the error early
                    {
                        child.Print();
                        Debug.WriteLine("Child plan: (cost {0})", child.allSingleAgentCosts[conflictingAgentIndex]);
                        child.allSingleAgentPlans[conflictingAgentIndex].PrintPlan();
                        Debug.WriteLine("Parent plan: (cost {0})", node.allSingleAgentCosts[conflictingAgentIndex]);
                        node.allSingleAgentPlans[conflictingAgentIndex].PrintPlan();
                        Debug.Assert(false, "Single agent node with lower cost than parent! " + child.totalCost + " < " + node.totalCost);
                    }

                    return child;
                }
                else
                {
                    this.closedListHits++;
                    closedListHitChildCost = this.closedList[child].totalCost;
                    if (debug)
                        Debug.WriteLine("Child already in closed list!");
                }
            }
            else
            {
                if (debug)
                    Debug.WriteLine("Child already generated before");
            }

            return null;
        }

        protected bool AdoptConditionally(CbsNode node, CbsNode adoptionCandidate, ushort nodeOrigH)
        {
            if (debug)
                Debug.WriteLine("Considering adoption of node hash: " + adoptionCandidate.GetHashCode() + ".");

            if (adoptionCandidate.totalCost == node.totalCost && // No need to branch :)
                adoptionCandidate.CompareToIgnoreH(node, true) == -1
               )
            {
                this.conflictsBypassed += node.totalConflictsWithExternalAgents + node.totalConflictsBetweenInternalAgents
                                                      - adoptionCandidate.totalConflictsWithExternalAgents - adoptionCandidate.totalConflictsBetweenInternalAgents;
                node.AdoptSolutionOf(adoptionCandidate);
                node.h = nodeOrigH; // Cancel partial expansion h boost

                if (debug)
                {
                    Debug.WriteLine("Child has same cost as parent and a better solution - child solution adopted by parent! (other generated children and partial expansions cancelled)");
                    Debug.WriteLine("Node new details:");
                    node.Print();
                }
                return true;
            }
            if (debug)
                Debug.WriteLine("Did not adopt.");
            return false;
        }

        protected virtual void addToGlobalConflictCount(CbsConflict conflict) { }

        public virtual Plan GetPlan()
        {
            if (this.solution == null)
                this.solution = this.goalNode.CalculateJointPlan();
            return this.solution;
        }

        public int GetSolutionDepth() { return this.solutionDepth; }
        
        public long GetMemoryUsed() { return Process.GetCurrentProcess().VirtualMemorySize64; }
        
        public SinglePlan[] GetSinglePlans()
        {
            return goalNode.allSingleAgentPlans;
        }

        public virtual int[] GetSingleCosts()
        {
            return goalNode.allSingleAgentCosts;
        }

        public int GetHighLevelExpanded() { return highLevelExpanded; }
        public int GetHighLevelGenerated() { return highLevelGenerated; }
        public int GetLowLevelExpanded() { return this.solver.GetAccumulatedExpanded(); }
        public int GetLowLevelGenerated() { return this.solver.GetAccumulatedGenerated(); }
        public int GetExpanded() { return highLevelExpanded; }
        public int GetGenerated() { return highLevelGenerated; }
        public int GetAccumulatedExpanded() { return accHLExpanded; }
        public int GetAccumulatedGenerated() { return accHLGenerated; }
        public int GetMaxGroupSize() { return this.maxSizeGroup; }
    }

    /// <summary>
    /// Merges agents if they conflict more times than the given threshold in all the CT.
    /// </summary>
    public class CBS_GlobalConflicts : CBS_LocalConflicts
    {
        public int[][] globalConflictsCounter;

        public CBS_GlobalConflicts(ICbsSolver singleAgentSolver, ICbsSolver generalSolver, int mergeThreshold = -1,
                                  bool doShuffle = false, BypassStrategy bypassStrategy = BypassStrategy.NONE, bool doMalte = false,
                                  ConflictChoice conflictChoice = ConflictChoice.FIRST,
                                  bool justbreakForConflicts = false, bool useMddHeuristic = false,
                                  int lookaheadMaxExpansions = int.MaxValue, bool mergeCausesRestart = false, int conflictRange = 0, Run.ConstraintPolicy constraintPolicy = Run.ConstraintPolicy.Single/*bool rangeConstraint = false*/)
            : base(singleAgentSolver, generalSolver, mergeThreshold, doShuffle, bypassStrategy, doMalte, conflictChoice,
                   justbreakForConflicts, useMddHeuristic, lookaheadMaxExpansions, mergeCausesRestart, conflictRange, constraintPolicy)
        {
            //throw new NotImplementedException("Not supported until we decide how to count conflicts. Used to rely on the specific conflict chosen in each node.");
        }

        /// <summary>
        /// Assumes agent nums start from 0 and are consecutive.
        /// </summary>
        /// <param name="problemInstance"></param>
        /// <param name="runner"></param>
        public override void Setup(ProblemInstance problemInstance, Run runner)
        {
            this.MakeConflictMatrix(problemInstance);
            base.Setup(problemInstance, runner);
        }

        private void MakeConflictMatrix(ProblemInstance problemInstance)
        {
            this.globalConflictsCounter = new int[problemInstance.m_vAgents.Length][];
            for (int i = 0; i < globalConflictsCounter.Length; i++)
            {
                this.globalConflictsCounter[i] = new int[i];
                for (int j = 0; j < i; j++)
                {
                    this.globalConflictsCounter[i][j] = 0;
                }
            }
        }

        protected override bool ShouldMerge(CbsNode node)
        {
            return node.ShouldMerge(mergeThreshold, globalConflictsCounter);
        }

        protected override void addToGlobalConflictCount(CbsConflict conflict)
        {
            if (conflict != null)
                globalConflictsCounter[Math.Max(conflict.agentAIndex, conflict.agentBIndex)][Math.Min(conflict.agentAIndex, conflict.agentBIndex)]++;
        }

        public override string GetName()
        {
            string baseName = base.GetName();
            return baseName.Replace("Local", "Global");
        }

        public override void Reset()
        {
            base.Reset();
            //this.MakeConflictMatrix(this.instance);
        }
    }
}
