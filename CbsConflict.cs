﻿
namespace CPF_experiment
{
    public class CbsConflict
    {
        public int agentAIndex; // Agent index and not agent num since this class is only used to represent internal conflicts
        public int agentBIndex;
        public Move agentAmove;
        public Move agentBmove;
        public int timeStep;
        public int timeStepAgentA;
        public int timeStepAgentB;
        /// <summary>
        /// If true, conflict is two agents have same dest, from any direction. Otherwise it's an edge conflict.
        /// </summary>
        public bool vertex;
        //public bool guaranteedCardinal;

        public CbsConflict(int conflictingAgentAIndex, int conflictingAgentBIndex, Move agentAMove, Move agentBMove, int timeStep, int timeStepAgentA = -1, int timeStepAgentB = -1)
        {
            this.agentAIndex = conflictingAgentAIndex;
            this.agentBIndex = conflictingAgentBIndex;
            this.agentAmove = agentAMove;
            this.agentBmove = agentBMove;
            this.timeStep = timeStep;
            this.timeStepAgentA = timeStepAgentA;
            this.timeStepAgentB = timeStepAgentB;
            if (agentAMove.x == agentBMove.x && agentAMove.y == agentBMove.y) // Same dest, from any direction
                this.vertex = true;
            else
                this.vertex = false;
            //this.guaranteedCardinal = false;
        }


        public override string ToString()
        {
            return "Agent " + this.agentAIndex + " going " + this.agentAmove + " collides with agent " + this.agentBIndex + " going " + this.agentBmove + " at time " + this.timeStep;
        }

        public override bool Equals(object obj)
        {
            if (this.agentAIndex != ((CbsConflict)obj).agentAIndex)
                return false;
            if (this.agentBIndex != ((CbsConflict)obj).agentBIndex)
                return false;
            if (this.vertex != ((CbsConflict)obj).vertex)
                return false;
            if (this.timeStep != ((CbsConflict)obj).timeStep)
                return false;
            if (this.vertex)
            { // Compare dests, ignore directions. Enough to compare one agent's move because the other is colliding with it.
                if (this.agentAmove.x != ((CbsConflict)obj).agentAmove.x)
                    return false;
                if (this.agentAmove.y != ((CbsConflict)obj).agentAmove.y)
                    return false;
            }
            else
            { // Compare dests and directions (unless direction is NO_DIRECTION)
                if (this.agentAmove.Equals(((CbsConflict)obj).agentAmove) == false)
                    return false;
                if (this.agentBmove.Equals(((CbsConflict)obj).agentBmove) == false)
                    return false;
            }               
            return true;
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return 2 * this.agentAIndex +
                       3 * this.agentBIndex +
                       5 * this.timeStep +
                       7 * this.vertex.GetHashCode() +
                       11 * this.agentAmove.GetHashCode() +
                       13 * this.agentBmove.GetHashCode();
            }
        }
    }
}
