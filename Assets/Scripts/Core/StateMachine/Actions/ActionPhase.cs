namespace Project.Core.StateMachine.Actions
{
    public enum ActionPhase
    {
        None = 0,
        Start = 1,
        Loop = 2,
        End = 3,
        Cancel = 4
    }
}
