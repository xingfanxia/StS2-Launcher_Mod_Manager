using System;
using Godot;
using STS2Mobile.Launcher.Components;

namespace STS2Mobile.Launcher.Sections;

public class LoginSection : VBoxContainer
{
    public event Action<string, string> LoginRequested;

    public LineEdit UsernameField { get; }
    private readonly LineEdit _passwordField;
    private readonly Button _loginButton;

    public LoginSection(float scale)
    {
        AddThemeConstantOverride("separation", (int)(6 * scale));
        Visible = false;
        VisibilityChanged += OnVisibilityChanged;

        UsernameField = new StyledLineEdit("Steam Username", scale);
        ConfigureAutofill(UsernameField, LoginAutofillField.Username);
        UsernameField.TextSubmitted += _ => _passwordField.GrabFocus();
        AddChild(UsernameField);

        _passwordField = new StyledLineEdit("Password", scale, secret: true);
        ConfigureAutofill(_passwordField, LoginAutofillField.Password);
        _passwordField.TextSubmitted += _ => OnLoginPressed();
        AddChild(_passwordField);

        _loginButton = new StyledButton("LOGIN", scale);
        _loginButton.Pressed += OnLoginPressed;
        AddChild(_loginButton);
    }

    public void SetDisabled(bool disabled)
    {
        _loginButton.Disabled = disabled;
    }

    public void ClearPassword()
    {
        _passwordField.Text = "";
    }

    private void OnLoginPressed()
    {
        AndroidLoginAutofillBridge.Clear();
        var username = UsernameField.Text.Trim();
        var password = _passwordField.Text;
        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            return;

        LoginRequested?.Invoke(username, password);
    }

    private static void ConfigureAutofill(LineEdit field, LoginAutofillField fieldType)
    {
        field.FocusEntered += () => AndroidLoginAutofillBridge.Configure(fieldType, field);
        field.GuiInput += inputEvent =>
        {
            var pointerPressed =
                inputEvent is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true }
                || inputEvent is InputEventScreenTouch { Pressed: true };
            if (pointerPressed)
                AndroidLoginAutofillBridge.Configure(fieldType, field);
        };
    }

    private void OnVisibilityChanged()
    {
        if (!IsVisibleInTree())
            AndroidLoginAutofillBridge.Clear();
    }
}
