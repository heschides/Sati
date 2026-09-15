using Sati.Views;
using System.Windows.Input;
using Xunit;

namespace Sati.Tests;

[Collection(WpfViewCollection.Name)]
public sealed class AttestationCheckBoxTests
{
    [Fact]
    public void ActivationRunsTheAttestationCommandWithoutChangingTheCheckmark()
    {
        WpfUiHarness.Run(() =>
        {
            var command = new RecordingCommand();
            var checkBox = new TestAttestationCheckBox
            {
                IsChecked = false,
                Command = command
            };

            checkBox.Activate();

            Assert.False(checkBox.IsChecked);
            Assert.Equal(1, command.ExecutionCount);

            checkBox.IsChecked = true;
            checkBox.Activate();

            Assert.True(checkBox.IsChecked);
            Assert.Equal(2, command.ExecutionCount);
        });
    }

    private sealed class TestAttestationCheckBox : AttestationCheckBox
    {
        internal void Activate() => OnClick();
    }

    private sealed class RecordingCommand : ICommand
    {
        public int ExecutionCount { get; private set; }
        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => ExecutionCount++;
    }
}
