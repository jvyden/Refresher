using System.Collections.Frozen;
using System.Diagnostics;
using Refresher.Core.Accessors;
using Refresher.Core.Patching;
using Refresher.Core.Pipelines.Steps;
using Refresher.Core.Verification.AutoDiscover;
using GlobalState = Refresher.Core.State;

namespace Refresher.Core.Pipelines;

public abstract class Pipeline
{
    public abstract string Id { get; }
    public abstract string Name { get; }
    
    public readonly Dictionary<string, string> Inputs = [];
    public FrozenSet<StepInput> RequiredInputs { get; private set; }

    internal List<GameInformation>? GameList { get; set; } = null;
    
    public IPatcher? Patcher { get; internal set; }
    public PatchAccessor? Accessor { get; internal set; }
    public GameInformation? GameInformation { get; internal set; }
    public EncryptionDetails? EncryptionDetails { get; internal set; }
    public AutoDiscoverResponse? AutoDiscover { get; internal set; }

    public virtual string? GuideLink => null;
    
    public PipelineState State { get; private set; } = PipelineState.NotStarted;
    
    public float Progress
    {
        get
        {
            if (this.State == PipelineState.Finished)
                return 1;
            
            float completed = (this._currentStepIndex - 1) / (float)this._stepCount;
            float currentStep = this._currentStep?.Progress ?? 0f;
            float stepWeight = 1f / this._stepCount;
            
            return completed + currentStep * stepWeight;
        }
    }

    public float CurrentProgress => this.State == PipelineState.Finished ? 1 : this._currentStep?.Progress ?? 0;

    protected virtual Type? SetupAccessorStepType => null;

    protected abstract List<Type> StepTypes { get; }
    private List<Step> _steps = [];
    
    private int _stepCount;
    private byte _currentStepIndex;
    private Step? _currentStep;

    public void Initialize()
    {
        List<StepInput> requiredInputs = [];
        
        this._steps = new List<Step>(this.StepTypes.Count + 1);
        
        if(this.SetupAccessorStepType != null)
            this.AddStep(requiredInputs, this.SetupAccessorStepType);

        foreach (Type type in this.StepTypes)
            this.AddStep(requiredInputs, type);
        
        this.RequiredInputs = requiredInputs.DistinctBy(i => i.Id).ToFrozenSet();
    }

    public void Reset()
    {
        this.Inputs.Clear();

        this.State = PipelineState.NotStarted;

        this._stepCount = 0;
        this._currentStepIndex = 0;
        this._currentStep = null;

        this.Patcher = null;
        if(this.Accessor is IDisposable disposable)
            disposable.Dispose();
        this.Accessor = null;
        this.GameInformation = null;
        this.EncryptionDetails = null;
    }

    private void AddStep(List<StepInput> requiredInputs, Type type)
    {
        Debug.Assert(type.IsAssignableTo(typeof(Step)));

        Step step = (Step)Activator.CreateInstance(type, this)!;

        this._steps.Add(step);
        requiredInputs.AddRange(step.Inputs);
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        if (this.State != PipelineState.NotStarted)
        {
            this.State = PipelineState.Error;
            throw new InvalidOperationException("Pipeline must be restarted before it can be executed again.");
        }
        
        foreach (StepInput input in this.RequiredInputs)
        {
            if(!this.Inputs.ContainsKey(input.Id))
                throw new InvalidOperationException($"Input {input.Id} was not provided to the pipeline before execution.");
        }

        GlobalState.Logger.LogInfo(LogType.Pipeline, $"Pipeline {this.GetType().Name} started.");
        this.State = PipelineState.Running;
        await this.RunListOfSteps(this._steps, cancellationToken);
        GlobalState.Logger.LogInfo(LogType.Pipeline, $"Pipeline {this.GetType().Name} finished!");
        this.State = PipelineState.Finished;
    }

    private async Task RunListOfSteps(List<Step> steps, CancellationToken cancellationToken = default)
    {
        this._stepCount = steps.Count;
        byte i = 1;
        foreach (Step step in steps)
        {
            GlobalState.Logger.LogInfo(LogType.Pipeline, $"Executing {step.GetType().Name}... ({i}/{steps.Count})");
            this._currentStepIndex = i;
            this._currentStep = step;

            try
            {
                await step.ExecuteAsync(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
            }
            catch (TaskCanceledException)
            {
                this.State = PipelineState.Cancelled;
                return;
            }
            catch (Exception)
            {
                this.State = PipelineState.Error;
                throw;
            }

            i++;
        }
    }

    public async Task<AutoDiscoverResponse?> InvokeAutoDiscoverAsync(string url, CancellationToken cancellationToken = default)
    {
        AutoDiscoverResponse? autoDiscover = await AutoDiscoverClient.InvokeAutoDiscoverAsync(url, cancellationToken);
        if(autoDiscover != null)
           this.AutoDiscover = autoDiscover;

        return autoDiscover;
    }

    public async Task<List<GameInformation>> DownloadGameListAsync(CancellationToken cancellationToken = default)
    {
        if (this.State != PipelineState.NotStarted)
        {
            this.State = PipelineState.Error;
            throw new InvalidOperationException("Pipeline must be in a clean state before downloading games.");
        }
        
        if(this.SetupAccessorStepType == null)
            throw new InvalidOperationException("This pipeline doesn't have accessors configured.");

        List<Step> stepTypes = [
            (Step)Activator.CreateInstance(this.SetupAccessorStepType, this)!,
            new DownloadGameListStep(this),
        ];
        
        this.State = PipelineState.Running;
        
        await this.RunListOfSteps(stepTypes, cancellationToken);
        
        this.Reset();

        if (this.GameList == null)
            throw new Exception("Could not download the list of games.");

        return this.GameList;
    }
}