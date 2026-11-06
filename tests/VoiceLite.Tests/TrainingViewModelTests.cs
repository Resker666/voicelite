using VoiceLite.Core;
using Xunit;

namespace VoiceLite.Tests;
public sealed class TrainingViewModelTests
{
    private static dynamic ViewModel(ITrainingService service)
    {
        var type = typeof(TrainingJob).Assembly.GetType("VoiceLite.Core.TrainingViewModel"); Assert.NotNull(type);
        return Activator.CreateInstance(type, service, Guid.NewGuid().ToString("N"))!;
    }
    [Fact] public async Task Existing_result_restores_reference_and_export_choice()
    {
        var service=new Stub(); dynamic vm=ViewModel(service); await vm.InitializeAsync();
        Assert.True((bool)vm.CanExport); Assert.Equal(service.Job.References[0].Id,(string)vm.SelectedReference.Id);
        await vm.ExportAsync("new.zip"); Assert.Equal("new.zip",service.Exported); Assert.Equal("new.zip",(string)vm.LastExportPath);
    }
    [Fact] public async Task Close_cancels_and_waits_for_the_owned_training_task()
    {
        var service=new Stub { Block=true }; dynamic vm=ViewModel(service); vm.EngineRoot="engine";
        Task running=vm.StartAsync(); await service.Started.Task;
        Assert.True((bool)vm.IsBusy); Assert.False((bool)vm.CanExport);
        Assert.True((bool)await vm.CloseAsync()); await running;
        Assert.True(service.Cancelled); Assert.False((bool)vm.IsBusy);
    }
    [Fact] public async Task Training_failure_is_shown_without_claiming_completion()
    {
        var service=new Stub { Fail=true }; dynamic vm=ViewModel(service); await vm.StartAsync();
        Assert.Contains("测试失败",(string)vm.Error); Assert.DoesNotContain("训练完成",(string)vm.Status);
        Assert.False((bool)vm.IsBusy);
    }
    [Fact] public async Task Result_without_a_suitable_reference_cannot_export()
    {
        var service=new Stub(); service.Job=service.Job with { References=[] }; dynamic vm=ViewModel(service); await vm.InitializeAsync();
        Assert.False((bool)vm.CanExport); Assert.Null((object?)vm.SelectedReference);
    }
    private sealed class Stub : ITrainingService
    {
        public bool Block,Fail,Cancelled; public string Exported="";
        public TaskCompletionSource Started=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TrainingJob Job=new(Guid.NewGuid().ToString("N"),Guid.NewGuid().ToString("N"),"版本","engine",TrainingJobState.Completed,8,[new(Guid.NewGuid().ToString("N"),"ref.wav",3,"参考文字","zh")]);
        public Task<IReadOnlyList<TrainingJob>> ListAsync(string datasetId,CancellationToken token=default)=>Task.FromResult<IReadOnlyList<TrainingJob>>([Job]);
        public async Task<TrainingJob> StartAsync(string dataset,string name,string engine,IProgress<string> progress,CancellationToken token=default)
        {
            Started.TrySetResult(); if(Fail)throw new VoiceLiteException("测试失败");
            try { if(Block)await Task.Delay(Timeout.Infinite,token); return Job; }
            catch(OperationCanceledException){Cancelled=true;throw;}
        }
        public Task<string> ExportRoleAsync(string jobId,string reference,string output,IProgress<string> progress,CancellationToken token=default)
        { Assert.Equal(Job.Id,jobId); Assert.Equal(Job.References[0].Id,reference); Exported=output;return Task.FromResult(output); }
    }
}
