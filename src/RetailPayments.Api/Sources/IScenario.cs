namespace RetailPayments.Api.Sources;

// A demo "step" the sources can be moved through (POST /api/dev/scenario/{step}) to show how the
// import reacts to changes. Registered only when there is something to step through.
public interface IScenario
{
    int Step { get; set; }

    // highest valid step; steps start at 0
    int MaxStep { get; }
}
