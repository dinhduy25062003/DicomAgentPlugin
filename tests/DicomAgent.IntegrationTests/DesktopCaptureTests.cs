using System.Windows.Input;
using DicomAgent.Application.Abstractions;
using DicomAgent.Application.Models;
using DicomAgent.Desktop.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DicomAgent.IntegrationTests;

public sealed class DesktopCaptureTests
{
    [Fact]
    public async Task StopCaptureOnlyStopsTheDisplayedActiveRequest()
    {
        await using var fixture = new TestFixture();
        using var api = fixture.Api();
        await api.StartAsync(default);
        try
        {
            var captures = fixture.Provider.GetRequiredService<ICaptureRequestService>();
            var input = new CaptureInput("crm.service", Guid.NewGuid(), "Test", "Test", "X-ray", "1", "Test patient");
            var first = await captures.ReceiveAsync(Guid.NewGuid(), "demo", "Bearer synthetic-token", input, default);
            var second = await captures.ReceiveAsync(Guid.NewGuid(), "demo", "Bearer synthetic-token",
                input with { ResId = Guid.NewGuid() }, default);
            var vm = new MainViewModel();
            await vm.RefreshCapturesAsync();
            Assert.False(vm.CanStopCapture);
            vm.ViewRequest(first);
            Assert.False(vm.CanStopCapture);

            await ExecuteAsync(vm.StartCaptureCommand);
            Assert.True(vm.CanStopCapture);
            Assert.Equal(first.Id, (await captures.SnapshotAsync(default)).ActiveRequestId);

            vm.ViewRequest(second);
            Assert.False(vm.CanStopCapture);
            await ExecuteAsync(vm.StopCaptureCommand);
            Assert.Equal(first.Id, (await captures.SnapshotAsync(default)).ActiveRequestId);

            vm.ViewRequest(first);
            Assert.True(vm.CanStopCapture);
            var disabledWhileStopping = false;
            vm.PropertyChanged += (_, change) =>
            {
                if (change.PropertyName == nameof(vm.CanStopCapture) && !vm.IsEditable)
                {
                    Assert.False(vm.CanStopCapture);
                    disabledWhileStopping = true;
                }
            };
            await ExecuteAsync(vm.StopCaptureCommand);
            Assert.True(disabledWhileStopping);
            Assert.False(vm.CanStopCapture);
            Assert.Null((await captures.SnapshotAsync(default)).ActiveRequestId);

            await captures.ActivateAsync(first.Id, default);
            await vm.RefreshCapturesAsync();
            Assert.True(vm.CanStopCapture);
            await captures.ActivateAsync(null, default);
            await captures.ActivateAsync(second.Id, default);
            await vm.RefreshCapturesAsync();
            Assert.False(vm.CanStopCapture);
            vm.ViewRequest(second);
            Assert.True(vm.CanStopCapture);
            await captures.RemoveAsync(second.Id, null, default);
            await vm.RefreshCapturesAsync();
            Assert.Null(vm.CurrentRequest);
            Assert.False(vm.CanStopCapture);
        }
        finally
        {
            await api.StopAsync(default);
        }
    }

    private static async Task ExecuteAsync(ICommand command)
    {
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnCanExecuteChanged(object? sender, EventArgs args)
        {
            if (command.CanExecute(null)) completed.TrySetResult();
        }
        command.CanExecuteChanged += OnCanExecuteChanged;
        try
        {
            command.Execute(null);
            await completed.Task.WaitAsync(TimeSpan.FromSeconds(15));
        }
        finally
        {
            command.CanExecuteChanged -= OnCanExecuteChanged;
        }
    }
}
