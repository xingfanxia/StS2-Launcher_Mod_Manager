using System;
using Godot;
using STS2Mobile.Launcher.Components;

namespace STS2Mobile.Launcher.Sections;

public class LoginSection : VBoxContainer
{
    public event Action<string, string> LoginRequested;
    public event Action AccountSwitchCancelled;
    public event Action QrLoginRequested;
    public event Action QrLoginCancelled;

    public LineEdit UsernameField { get; }
    private readonly LineEdit _passwordField;
    private readonly Button _loginButton;
    private readonly Button _cancelSwitchButton;
    private readonly Button _qrButton;
    private readonly Button _qrCancelButton;
    private readonly Label _qrHint;
    private readonly TextureRect _qrImage;

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

        // Offered only where the platform can draw the code.
        _qrButton = new StyledButton("SIGN IN WITH QR CODE", scale);
        _qrButton.Visible = PlatformHost.QrCodeRenderer != null;
        _qrButton.Pressed += () => QrLoginRequested?.Invoke();
        AddChild(_qrButton);

        _qrHint = new StyledLabel(
            "Scan with the Steam mobile app, then approve the sign-in.",
            scale,
            fontSize: 14
        );
        _qrHint.Visible = false;
        AddChild(_qrHint);

        _qrImage = new TextureRect
        {
            Visible = false,
            CustomMinimumSize = new Vector2(220 * scale, 220 * scale),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            TextureFilter = TextureFilterEnum.Nearest,
        };
        AddChild(_qrImage);

        _qrCancelButton = new StyledButton("Cancel", scale);
        _qrCancelButton.Visible = false;
        _qrCancelButton.Pressed += () => QrLoginCancelled?.Invoke();
        AddChild(_qrCancelButton);

        _cancelSwitchButton = new StyledButton("계정 전환 취소", scale);
        _cancelSwitchButton.Visible = false;
        _cancelSwitchButton.Pressed += () => AccountSwitchCancelled?.Invoke();
        AddChild(_cancelSwitchButton);
    }

    public void SetDisabled(bool disabled)
    {
        _loginButton.Disabled = disabled;
        _qrButton.Disabled = disabled;
        if (!disabled)
            SetQrVisible(false);
    }

    // Shows the QR code for the current Steam challenge and hides the password form.
    public void ShowQrCode(Texture2D code)
    {
        _qrImage.Texture = code;
        SetQrVisible(true);
    }

    private void SetQrVisible(bool visible)
    {
        _qrImage.Visible = visible;
        _qrHint.Visible = visible;
        _qrCancelButton.Visible = visible;
        UsernameField.Visible = !visible;
        _passwordField.Visible = !visible;
        _loginButton.Visible = !visible;
        _qrButton.Visible = !visible && PlatformHost.QrCodeRenderer != null;
        if (!visible)
            _qrImage.Texture = null;
    }

    public void ClearPassword()
    {
        _passwordField.Text = "";
    }

    public void SetAccountSwitchMode(bool enabled)
    {
        UsernameField.Text = "";
        _passwordField.Text = "";
        _cancelSwitchButton.Visible = enabled;
        AndroidLoginAutofillBridge.Clear();
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
