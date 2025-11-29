using Openctrol.Agent.Input;
using Openctrol.Agent.Logging;
using Xunit;

namespace Openctrol.Agent.Tests;

public class InputDispatcherTests
{
    [Fact]
    public void DispatchPointer_MoveRelative_CreatesCorrectInput()
    {
        // Arrange
        var logger = new CompositeLogger(new NullLogger());
        var dispatcher = new InputDispatcher(logger);
        var evt = new PointerEvent
        {
            Kind = PointerEventKind.MoveRelative,
            Dx = 10,
            Dy = -5
        };

        // Act & Assert - Should not throw
        dispatcher.DispatchPointer(evt);
    }

    [Fact]
    public void DispatchPointer_MoveAbsolute_CreatesCorrectInput()
    {
        // Arrange
        var logger = new CompositeLogger(new NullLogger());
        var dispatcher = new InputDispatcher(logger);
        var evt = new PointerEvent
        {
            Kind = PointerEventKind.MoveAbsolute,
            AbsoluteX = 100,
            AbsoluteY = 200
        };

        // Act & Assert - Should not throw
        dispatcher.DispatchPointer(evt);
    }

    [Fact]
    public void DispatchPointer_Button_CreatesCorrectInput()
    {
        // Arrange
        var logger = new CompositeLogger(new NullLogger());
        var dispatcher = new InputDispatcher(logger);
        var evt = new PointerEvent
        {
            Kind = PointerEventKind.Button,
            Button = MouseButton.Left,
            ButtonAction = MouseButtonAction.Down
        };

        // Act & Assert - Should not throw
        dispatcher.DispatchPointer(evt);
    }

    [Fact]
    public void DispatchKeyboard_KeyDown_CreatesCorrectInput()
    {
        // Arrange
        var logger = new CompositeLogger(new NullLogger());
        var dispatcher = new InputDispatcher(logger);
        var evt = new KeyboardEvent
        {
            Kind = KeyboardEventKind.KeyDown,
            KeyCode = 65, // 'A' key
            Modifiers = KeyModifiers.None
        };

        // Act & Assert - Should not throw
        dispatcher.DispatchKeyboard(evt);
    }

    [Fact]
    public void DispatchKeyboard_KeyDown_WithModifiers_CreatesCorrectInput()
    {
        // Arrange
        var logger = new CompositeLogger(new NullLogger());
        var dispatcher = new InputDispatcher(logger);
        var evt = new KeyboardEvent
        {
            Kind = KeyboardEventKind.KeyDown,
            KeyCode = 65, // 'A' key
            Modifiers = KeyModifiers.Ctrl | KeyModifiers.Shift
        };

        // Act & Assert - Should not throw
        dispatcher.DispatchKeyboard(evt);
    }

    [Fact]
    public void DispatchKeyboard_Text_CreatesCorrectInput()
    {
        // Arrange
        var logger = new CompositeLogger(new NullLogger());
        var dispatcher = new InputDispatcher(logger);
        var evt = new KeyboardEvent
        {
            Kind = KeyboardEventKind.Text,
            Text = "Hello",
            Modifiers = KeyModifiers.None
        };

        // Act & Assert - Should not throw
        dispatcher.DispatchKeyboard(evt);
    }

    [Fact]
    public void DispatchKeyboard_Text_WithModifiers_CreatesCorrectInput()
    {
        // Arrange
        var logger = new CompositeLogger(new NullLogger());
        var dispatcher = new InputDispatcher(logger);
        var evt = new KeyboardEvent
        {
            Kind = KeyboardEventKind.Text,
            Text = "A",
            Modifiers = KeyModifiers.Ctrl
        };

        // Act & Assert - Should not throw
        dispatcher.DispatchKeyboard(evt);
    }

    // Helper class for testing
    private class NullLogger : ILogger
    {
        public void Info(string message) { }
        public void Warn(string message) { }
        public void Error(string message, Exception? ex = null) { }
    }
}

