﻿using System;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;

namespace CPF_experiment
{
    /// <summary>
    /// Describes a node in the A* search space.
    /// </summary>
    public class WorldState : IComparable<IBinaryHeapItem> , IBinaryHeapItem
    {
        public int makespan; // Total time steps passed, max(agent makespans)
        public int g; // Sum of agent makespans until they reach their goal
        public int h;
        public int hBonus;
        public AgentState[] allAgentsState;
        public WorldState prevStep;
        private int binaryHeapIndex;
        /// <summary>
        /// For Independence Detection only
        /// </summary>
        public int potentialConflictsCount;
        public int cbsInternalConflictsCount;
        /// <summary>
        /// Maps from agent num to the number of times the path up to this node collides with that agent
        /// </summary>
        public Dictionary<int, int> cbsInternalConflicts;
        /// <summary>
        /// Maps from agent num to the time of the first conflict with it
        /// </summary>
        public Dictionary<int, List<int>> conflictTimes;
        public Dictionary<int, List<int>> conflictTimesBias;
        /// <summary>
        /// The min depth (makespan) from which a node may be considered a goal.
        /// TODO: Consider moving out of the node object to a static variable or something.
        ///       It doesn't change between nodes.
        /// </summary>
        public int minDepth;
        /// <summary>
        /// The min cost (g) from which a node may be considered a goal.
        /// TODO: Consider moving out of the node object to a static variable or something.
        ///       It doesn't change between nodes.
        /// </summary>
        public int minCost;
        /// <summary>
        /// The last move of all agents that have already moved in this turn.
        /// Used for making sure the next agent move doesn't collide with moves already made.
        /// </summary>
        public Dictionary<TimedMove, int> currentMoves;
        protected static readonly int NOT_SET = -1;
        /// <summary>
        /// For computing expansion delay
        /// </summary>
        public int expandedCountWhenGenerated;
        ///// <summary>
        ///// For lazy heuristics
        ///// </summary>
        //public CBS_LocalConflicts cbsState;
        /// <summary>
        /// For MStar.
        /// Disjoint sets of agent indices, since only internal agents are considered.
        /// </summary>
        public DisjointSets<int> collisionSets;
        //public ISet<int> currentCollisionSet;
        public ISet<WorldState> backPropagationSet;
        public TimedMove[] plannedMoves;
        public int conflictRange;
        /// <summary>
        /// Create a state with the given state for every agent.
        /// </summary>
        /// <param name="allAgentsState"></param>
        public WorldState(AgentState[] allAgentsState, int minDepth = -1, int minCost = -1, int conflictRange = 0)
        {
            this.allAgentsState = allAgentsState.ToArray<AgentState>();
            this.makespan = allAgentsState.Max<AgentState>(state => state.lastMove.time); // We expect to only find at most two G values within the agent group
            this.CalculateG(); // G not necessarily zero when solving a partially solved problem.
            this.potentialConflictsCount = 0;
            this.cbsInternalConflictsCount = 0;
            this.cbsInternalConflicts = new Dictionary<int, int>();
            this.conflictTimes = new Dictionary<int, List<int>>();
            this.conflictTimesBias = new Dictionary<int, List<int>>();
            this.minDepth = minDepth;
            this.minCost = minCost;
            this.currentMoves = new Dictionary<TimedMove, int>();
            this.goalCost = NOT_SET;
            this.goalSingleCosts = null;
            this.singlePlans = null;
            this.hBonus = 0;
            this.conflictRange = conflictRange;
        }

        /// <summary>
        /// Copy constructor.
        /// </summary>
        /// <param name="cpy"></param>
        public WorldState(WorldState cpy)
        {
            this.makespan = cpy.makespan;
            this.g = cpy.g;
            this.h = cpy.h;
            // The potentialConflictsCount, conflictTimes, cbsInternalConflicts and cbsInternalConflictsCount are only copied later if necessary.
            this.minDepth = cpy.minDepth;
            this.minCost = cpy.minCost;
            this.allAgentsState = new AgentState[cpy.allAgentsState.Length];
            for (int i = 0; i < allAgentsState.Length; i++)
            {
                this.allAgentsState[i] = new AgentState(cpy.allAgentsState[i]); // Shallow copy - it's still the same lastMove inside. Why a copy?
            }
            this.currentMoves = new Dictionary<TimedMove, int>(cpy.currentMoves);
            this.goalCost = NOT_SET;
            this.goalSingleCosts = null;
            this.singlePlans = null;
            this.hBonus = 0;
            this.conflictRange = conflictRange;
        }

        /// <summary>
        /// Creates a new state by extracting a subset of the agents from
        /// the original Trevor_WorldState. We overload the constructor because
        /// while building our pattern database, we rewrite the problem and
        /// therefore need to make a deep copy of the state data structures so
        /// as to not overwrite the original problem. The ultimate solution
        /// would be to rework the code to remove static variables so that we
        /// can instantiate subproblems without affecting the original data
        /// structures.
        /// </summary>
        /// <param name="allAgentsState">A set of agent states in the original problem.</param>
        /// <param name="vAgents">A list of indices referring to the subset of agents we want to extract.</param>
        public WorldState(AgentState[] allAgentsState, List<uint> vAgents)
            // Copy specified agents only
            : this(vAgents.Select<uint, AgentState>(index => new AgentState(allAgentsState[index])).ToArray<AgentState>())
        {}
        
        public bool GoalTest()
        {
            // Check if this is a generalised goal node and its plan is long enough.
            // If we know the optimal solution, it doesn't matter if this is a real goal node or not, we can finish.
            if (this.singlePlans != null)
            {
                // Check if plans are long enough and costly enough
                if (this.singlePlans.All<SinglePlan>(plan => plan.GetSize() - 1 >= this.minDepth))
                {
                    if (this.singlePlans.Sum<SinglePlan>(plan => plan.GetCost()) >= this.minCost)
                        return true;
                }
            }

            if (this.g < this.minCost)
                return false;

            if (this.makespan < this.minDepth)
                return false;

            return this.h == 0; // That's crazy! A node that is close to the goal might also get h==0.
                                // Our specific heuristic doesn't behave that way, though.
                                // Not crazy, just assumes the heuristic is consistent, which has the property that only the goal has h==0.
                                // SIC really is a consistent heuristic.
                                // FIXME: Implement a proper goal test and use it when h==0.
        }

        protected SinglePlan[] singlePlans;

        /// <summary>
        /// Set the optimal solution of this node as a problem instance.
        /// </summary>
        /// <param name="solution"></param>
        public virtual void SetSolution(SinglePlan[] solution)
        {
            this.singlePlans = SinglePlan.GetSinglePlans(this); // This node may be a partial solution itself, need to start from the real root.
            for (int i = 0; i < solution.Length; ++i)
                this.singlePlans[i].ContinueWith(solution[i]);
        }

        public SinglePlan[] GetSinglePlans()
        {
            if (this.singlePlans != null)
                return this.singlePlans;
            else
                return SinglePlan.GetSinglePlans(this);
        }

        /// <summary>
        /// Returns the optimal plan to the goal through this node, if this is a goal node (of any kind),
        /// else returns the optimal plan to this node.
        /// </summary>
        /// <returns></returns>
        public Plan GetPlan()
        {
            if (this.singlePlans != null)
                return new Plan(this.singlePlans);
            else
                return new Plan(this);
        }

        protected int goalCost; // TODO: Get rid of this and just return the sum of the single costs where needed.

        /// <summary>
        /// Returns the optimal cost to the goal from the start through this node.
        /// </summary>
        /// <returns></returns>
        public int GetGoalCost()
        {
            Debug.Assert(this.GoalTest(), "Only call for goal nodes!");

            if (goalCost == NOT_SET) // This is just a proper goal
                return this.g;
            else                     // This is a generalised goal node - it stores the optimal path to the goal through it
                return this.goalCost;
        }

        /// <summary>
        /// Set the optimal cost from the start to the goal through this node
        /// </summary>
        /// <param name="cost"></param>
        public void SetGoalCost(int cost)
        {
            this.goalCost = cost;
        }

        protected int[] goalSingleCosts;

        public int[] GetSingleCosts()
        {
            Debug.Assert(this.GoalTest(), "Only call for goal nodes!");

            if (goalSingleCosts == null) // This is just a proper goal
                return allAgentsState.Select<AgentState, int>(agent => agent.g).ToArray<int>();
            else
                return this.goalSingleCosts;
        }

        /// <summary>
        /// Set the optimal cost from the start to the goal through this node for every agent
        /// 
        /// </summary>
        /// <param name="cost"></param>
        public void SetSingleCosts(int[] costs)
        {
            this.goalSingleCosts = costs;
        }

        /// <summary>
        /// Used when WorldState objects are put in the open list priority queue
        /// </summary>
        /// <param name="other"></param>
        /// <returns></returns>
        public virtual int CompareTo(IBinaryHeapItem other)
        {
            WorldState that = (WorldState)other;
            int thisF = this.h + this.g;
            int thatF = that.h + that.g;
            if (thisF < thatF)
                return -1;
            if (thisF > thatF)
                return 1;

            // Tie breaking:
            bool thisIsGoal = this.GoalTest();
            bool thatIsGoal = that.GoalTest();
            if (thisIsGoal == true && thatIsGoal == false) // The elaborate form is necessary to keep the comparison consistent. Otherwise goalA<goalB and goalB<goalA
                return -1;
            if (thatIsGoal == true && thisIsGoal == false)
                return 1;

            // Independence Detection framework conflicts:
            if (this.potentialConflictsCount < that.potentialConflictsCount)
                return -1;
            if (this.potentialConflictsCount > that.potentialConflictsCount)
                return 1;

            // CBS framework conflicts:
            // It makes sense to prefer nodes that conflict less, and not just nodes that don't conflict at all,
            // because a 3-way conflict takes more work to resolve than
            if (this.cbsInternalConflictsCount < that.cbsInternalConflictsCount)
                return -1;
            if (this.cbsInternalConflictsCount > that.cbsInternalConflictsCount)
                return 1;

            // //M-Star: prefer nodes with smaller collision sets:
            //if (this.collisionSets != null) // than M-Star is running
            //{
            //    // The collision sets change during collision set backpropagation and closed list hits.
            //    // Backpropagation goes from a node's child to the node, so it's tempting to think
            //    // it only happens when the node is already expanded and out of the open list,
            //    // but partial expansion makes that false. 
            //    // Closed list hits can also happen while the node is waiting to be expanded.
            //    // So the max rank can change while the node is in the open list - 
            //    // it can't be used for tie breaking :(.
            //    if (this.collisionSets.maxRank < that.collisionSets.maxRank)
            //        return -1;
            //    if (that.collisionSets.maxRank > this.collisionSets.maxRank)
            //        return 1;
            //}

            // f, collision sets, conflicts and internal conflicts being equal, prefer nodes with a larger g
            // - they're closer to the goal so less nodes would probably be generated by them on the way to it.
            if (this.g < that.g)
                return 1;
            if (this.g > that.g)
                return -1;

            return 0;
        }

        /// <summary>
        /// Calculate and set the g of the state as the sum of the different agent g values.
        /// </summary>
        public virtual void CalculateG()
        {
            g = allAgentsState.Sum<AgentState>(agent => agent.g);
        }

        /// <summary>
        /// Prepare for re-insertion into the open list
        /// </summary>
        public virtual void Clear() { }

        public virtual int f
        {
            get
            {
                return this.g + this.h;
            }
        }

        public override string ToString()
        {
            string ans = "makespan: " + makespan + ", h: " + h + ", g: " + g;
            ans += " ";
            foreach (AgentState temp in allAgentsState)
            {
                //ans +="\n agent " + temp.agent.agentNum + ": " + temp.lastMove;
                //ans += " agent " + temp.agent.agentNum + ": " + temp.lastMove;
                ans += "|" + temp.lastMove;
            }
            ans += "|";
            return ans;
        }

        /// <summary>
        /// Returns the last move of all the agents in this state.
        /// </summary>
        /// <returns>A list of Moves</returns>
        public List<Move> GetAgentsMoves()
        {
            return this.allAgentsState.Select<AgentState, Move>(state => state.lastMove).ToList<Move>();
        }

        /// <summary>
        /// Returns the last move of the requested agent.
        /// </summary>
        /// <param name="index"></param>
        /// <returns></returns>
        public Move GetSingleAgentMove(int index)
        {
            return allAgentsState[index].lastMove;
        }

        /// <summary>
        /// BH_Item implementation
        /// </summary>
        /// <returns></returns>
        public int GetIndexInHeap() { return binaryHeapIndex; }

        /// <summary>
        /// BH_Item implementation
        /// </summary>
        /// <returns></returns>
        public void SetIndexInHeap(int index) { binaryHeapIndex = index; }

        /// <summary>
        /// Checks for internal conflicts
        /// </summary>
        /// <returns></returns>
        public bool isValid()
        {
            for (int i = 0; i < this.allAgentsState.Length; i++)
            {
                for (int j = i+1; j < this.allAgentsState.Length; j++)
                {
                    // Internal conflict
                    if (this.allAgentsState[i].lastMove.IsColliding(this.allAgentsState[j].lastMove))
                        return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Only the agent states are used in the hash.
        /// The g, makespan, h, potentialConflictsCount, cbsInternalConflictsCount and others are ignored, as neccesary.
        /// </summary>
        /// <returns></returns>
        public override int GetHashCode()
        {
            int ans = 0;
            unchecked
            {
                for (int i = 0 ; i < allAgentsState.Length; i++)
                {
                    ans += allAgentsState[i].GetHashCode() * Constants.PRIMES_FOR_HASHING[i % Constants.PRIMES_FOR_HASHING.Length];
                }
            }
            return ans;
        }

        /// <summary>
        /// Only the AgentStates are compared.
        /// g, makespan, h, potentialConflictsCount, cbsInternalConflictsCount and others are ignored, as necessary.
        /// </summary>
        /// <param name="obj"></param>
        /// <returns></returns>
        public override bool Equals(object obj)
        {
            if (obj == null)
                return false;
            WorldState that = (WorldState)obj;
            return this.allAgentsState.SequenceEqual<AgentState>(that.allAgentsState);
        }

        /// <summary>
        /// Counts the number of times this node collides with each agent move in the conflict avoidance table.
        /// </summary>
        /// <param name="conflictAvoidance"></param>
        /// <returns></returns>
        public virtual void UpdateConflictCounts(IReadOnlyDictionary<TimedMove, List<int>> conflictAvoidance, int conflictRange = 0)
        {
            for (int i = 0; i < this.allAgentsState.Length; i++)
            {
                this.allAgentsState[i].lastMove.UpdateConflictCounts(conflictAvoidance, this.cbsInternalConflicts, this.conflictTimes, this.conflictTimesBias, conflictRange);
            }
        }

        public virtual ProblemInstance ToProblemInstance(ProblemInstance initial)
        {
            // Notice this is not a subproblem in the number of agents but
            // in the steps from the start.
            // It might even be harder if the steps were away from the goal.
            return initial.Subproblem(this.allAgentsState);
        }

        //public WorldState GetPlanStart(int agentIndex)
        //{
        //    WorldState node = this;
        //    while (node.individualMStarBookmarks[agentIndex] != 0)
        //        node = node.prevStep;
        //    return node;
        //}
    }
}
