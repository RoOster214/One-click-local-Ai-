using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Text.Json;

namespace PhoenixOfflineAI
{
    public partial class PhoenixOfflineUi : Form
    {
        #region Constants
        private const int MAX_FILE_SIZE_BYTES = 10 * 1024 * 1024; // 10MB
        private const int MAX_DOCUMENT_LENGTH = 20000; // 20k characters
        private const int MAX_FILES_ALLOWED = 10;
        private const int HTTP_TIMEOUT_MINUTES = 3;
        private const string OLLAMA_BASE_URL = "http://localhost:11434";
        private const string OLLAMA_GENERATE_ENDPOINT = "/api/generate";
        private const int MAX_REQUESTS_PER_MINUTE = 20;
        private const int MAX_INPUT_LENGTH = 4000;

        // Model constants
        private const string MODEL_VISION = "llava:7b";
        private const string MODEL_CODE = "codellama:7b";
        private const string MODEL_GENERAL = "llama3.2:3B";
        private const string MODEL_DEFAULT = MODEL_GENERAL; // Default model to use
        #endregion

        #region Fields
        private readonly HttpClient _httpClient;
        private readonly OpenFileDialog _openFileDialog;
        private readonly List<FileAttachment> _attachedFiles;
        private SecureLogger _logger;
        private SecureAppConfig _config;
        private SecureChatHistory _chatHistory;

        // Rate limiting
        private DateTime _lastRequestTime = DateTime.MinValue;
        private int _requestCount = 0;
        private readonly object _rateLimitLock = new object();


        private FlowLayoutPanel _chatListPanel;
        private Panel pnlWelcome = null!; // Welcome panel  
        private Panel pnlChat = null!; // Main chat panel
        private Panel _leftSidebar; // Reference to the sidebar
        private bool _sidebarVisible = true; // Track sidebar state
        private DateTime _lastBackClick = DateTime.MinValue; // Track timing for double-click
        private const int DOUBLE_CLICK_TIME_MS = 500; // 500ms window for double-click                               
        private RichTextBox rtbChatDisplay = null!;
        private TextBox initialMessageTextBox = null!; // Initial message input box
        private TextBox OfflineAiChatBox = null!;
        private Button SendButton = null!;
        private Button ImageUploadButton = null!;
        private Button DocumentUploadButton = null!;
        private Button startButton = null!;

        
        private bool _isProcessing;
        #endregion
        #region Color Constants
        public enum AppTheme
        {
            Dark,
            Light
        }

        private static class FeniColors
        {
            public static AppTheme CurrentTheme = AppTheme.Dark;

            // Dark theme colors (your current ones)
            public static readonly Color DarkMainBackground = Color.FromArgb(32, 32, 32);
            public static readonly Color DarkSidebarBackground = Color.FromArgb(17, 17, 17);
            public static readonly Color DarkTextPrimary = Color.WhiteSmoke;
            public static readonly Color DarkTextSecondary = Color.FromArgb(150, 150, 150);
            public static readonly Color DarkInputBackground = Color.FromArgb(45, 45, 48);
            public static readonly Color DarkHoverColor = Color.FromArgb(45, 45, 45);

            // Light theme colors
            public static readonly Color LightMainBackground = Color.FromArgb(255, 255, 255);
            public static readonly Color LightSidebarBackground = Color.FromArgb(245, 245, 245);
            public static readonly Color LightTextPrimary = Color.FromArgb(33, 33, 33);
            public static readonly Color LightTextSecondary = Color.FromArgb(100, 100, 100);
            public static readonly Color LightInputBackground = Color.FromArgb(240, 240, 240);
            public static readonly Color LightHoverColor = Color.FromArgb(230, 230, 230);

            // Common colors
            public static readonly Color AccentRed = Color.FromArgb(180, 50, 50);
            public static readonly Color UserMessageBg = Color.FromArgb(55, 65, 81);

            // Dynamic properties that return colors based on current theme
            public static Color MainBackground => CurrentTheme == AppTheme.Dark ? DarkMainBackground : LightMainBackground;
            public static Color SidebarBackground => CurrentTheme == AppTheme.Dark ? DarkSidebarBackground : LightSidebarBackground;
            public static Color TextPrimary => CurrentTheme == AppTheme.Dark ? DarkTextPrimary : LightTextPrimary;
            public static Color TextSecondary => CurrentTheme == AppTheme.Dark ? DarkTextSecondary : LightTextSecondary;
            public static Color InputBackground => CurrentTheme == AppTheme.Dark ? DarkInputBackground : LightInputBackground;
            public static Color HoverColor => CurrentTheme == AppTheme.Dark ? DarkHoverColor : LightHoverColor;
        }
        #endregion
        #region Constructor & Initialization




        public PhoenixOfflineUi()
        
        {
        
            InitializeComponent();
        

            // Initialize security components first
            _logger = new SecureLogger();
            _config = new SecureAppConfig();
            _chatHistory = new SecureChatHistory(_logger, _config);

            // Initialize all the components
            _httpClient = CreateSecureHttpClient();
            _openFileDialog = new OpenFileDialog();
            _attachedFiles = new List<FileAttachment>();

            InitializeUI();

            _logger.LogInfo("Security features enabled");
        }

        private HttpClient CreateSecureHttpClient()
        {
            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (sender, cert, chain, errors) =>
                {
                    // Only allow connections to localhost
                    var requestUri = sender as HttpRequestMessage;
                    if (requestUri?.RequestUri?.Host == "localhost") return true;
                    return false;
                }
            };

            var client = new HttpClient(handler);
            client.BaseAddress = new Uri(OLLAMA_BASE_URL);
            client.Timeout = TimeSpan.FromMinutes(HTTP_TIMEOUT_MINUTES);
            client.DefaultRequestHeaders.Add("User-Agent", "Feni.AI/1.0");
            return client;
        }

        private void InitializeUI()
        {
            try
            {
                // Set basic form properties first - this gives the form proper dimensions
                this.Text = "Feni";
                this.BackColor = Color.Black;
                this.StartPosition = FormStartPosition.CenterScreen;
                this.Size = new Size(550, 500); // Compact and modern
                this.MinimumSize = new Size(600, 600); // Small minimum

                _isProcessing = false;

                // Initialize the panel-based UI structure
                InitializePanels();

                // Update UI state
                UpdateUIState();

                _logger.LogInfo("UI initialized successfully with panel-based interface");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to initialize UI: {ex.Message}");
                MessageBox.Show($"UI Initialization Error: {ex.Message}", "Feni AI",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void InitializePanels()
        {
            // Create welcome panel
            pnlWelcome = new Panel
            {
                Name = "pnlWelcome",
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(32, 32, 32)
            };

            // Create chat panel (initially hidden)
            pnlChat = new Panel
            {
                Name = "pnlChat",
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(32, 32, 32),
                Visible = false
            };

            // Add panels to form
            this.Controls.Add(pnlWelcome);
            this.Controls.Add(pnlChat);

            // Ensure chat panel is on top when visible
            pnlChat.BringToFront();

            // Move existing controls to welcome panel
            MoveExistingControlsToWelcomePanel();

            // Set up welcome panel
            SetupWelcomePanel();

            // DON'T set up chat interface yet - wait until it's needed
            // This prevents the splitter error during initial load

            _logger.LogInfo("Panels initialized successfully");
            UpdateUIState();
        }
        private void SetupInputArea()
        {
            if (OfflineAiChatBox != null)
            {
                OfflineAiChatBox.ReadOnly = false; // Enable text input
                OfflineAiChatBox.ForeColor = Color.Black;
                OfflineAiChatBox.Text = ""; // Start empty, ready to type
                OfflineAiChatBox.Focus();
            }
        }

        private void MoveExistingControlsToWelcomePanel()
        {
            // Collect controls to move (except our new panels)
            var controlsToMove = new List<Control>();
            foreach (Control control in this.Controls)
            {
                if (control != pnlWelcome && control != pnlChat)
                {
                    controlsToMove.Add(control);
                }
            }
            // Move controls to welcome panel
            foreach (Control control in controlsToMove)
            {
                this.Controls.Remove(control);
                pnlWelcome.Controls.Add(control);

            }
            // Set welcome panel as the main visible panel
            _logger.LogInfo($"Moved {controlsToMove.Count} controls to welcome panel");
        }
        private void SetupWelcomePanel()
        {
            // Clear any existing content to ensure we start with a clean welcome screen
            // This prevents any unexpected layering of old and new content
            pnlWelcome.Controls.Clear();

            // Create the main title that establishes your application's identity
            // Using a large, bold font creates immediate visual impact and brand recognition
            Label titleLabel = new Label
            {
                Text = "✴️",
                Font = new Font("Lucida Sans Unicode", 46, FontStyle.Bold),
                ForeColor = Color.Brown, // Firebrick to match Phoenix theme
                BackColor = Color.Transparent,
                AutoSize = true,
                TextAlign = ContentAlignment.MiddleCenter
                
            };
            titleLabel.MouseEnter += (sender, e) => {
                // Add a click event to the title for interactivity
               
                titleLabel.Text = "✳️"; // Change text on hover
            };
            titleLabel.MouseLeave += (sender, e) => {
                titleLabel.Text = "✴️"; //  text resets to create visual effect
            

        };
            // Create a subtitle that immediately communicates value to users
            // This answers the critical question: "What does this application do for me?"
            Label subtitleLabel = new Label
            {
                Text = " Feni, Vidi, Vici! ",
                Font = new Font("Georgia", 24, FontStyle.Bold),
                ForeColor = Color.DimGray,
                BackColor = Color.Transparent,
                AutoSize = true,
                TextAlign = ContentAlignment.MiddleCenter
            };

            this.initialMessageTextBox = new System.Windows.Forms.TextBox();
            this.initialMessageTextBox.Multiline = true;
            this.initialMessageTextBox.ScrollBars = ScrollBars.None;
            this.initialMessageTextBox.Font = new System.Drawing.Font("Georgia", 12F, System.Drawing.FontStyle.Regular);
            this.initialMessageTextBox.ForeColor = System.Drawing.Color.WhiteSmoke;
            this.initialMessageTextBox.BackColor = System.Drawing.Color.FromArgb(45, 45, 48);
            this.initialMessageTextBox.BorderStyle = BorderStyle.FixedSingle;
            this.initialMessageTextBox.Size = new System.Drawing.Size(400, 80);
            this.initialMessageTextBox.Location = new System.Drawing.Point(112, 280); // Adjust based on your layout
            this.initialMessageTextBox.Text = "";
            this.initialMessageTextBox.Enter += (s, e) => {
                if (this.initialMessageTextBox.Text == "")
                {
                    this.initialMessageTextBox.Text = "";
                    this.initialMessageTextBox.ForeColor = System.Drawing.Color.White;
                }
            };
            this.initialMessageTextBox.Leave += (s, e) => {
                if (string.IsNullOrWhiteSpace(this.initialMessageTextBox.Text))
                {
                    this.initialMessageTextBox.Text = "";
                    this.initialMessageTextBox.ForeColor = System.Drawing.Color.Gray;
                }
            };

            // Create the primary call-to-action button with professional styling
            // This button serves as the gateway between your welcome screen and chat functionality
            Button startButton = new Button
            {
                Text = "🔍",
                Size = new Size(55, 35),
                Font = new Font("Segoe UI", 13, FontStyle.Bold),
                BackColor = Color.DimGray,// OrangeRed to match theme       
                ForeColor = Color.WhiteSmoke,
                FlatStyle = FlatStyle.Popup,
                Cursor = Cursors.Hand, // Hand cursor for interactivity
               
            };

            // Remove the default border for a modern, clean appearance
            startButton.FlatAppearance.BorderSize = 0;

            // Add hover effects that provide immediate visual feedback
            // These micro-interactions make the interface feel responsive and professional
            startButton.MouseEnter += (sender, e) => {
                startButton.ForeColor = Color.LightGray; // Change text color on hover
                startButton.BackColor = Color.FromArgb(180, 50, 50); // Darkens on hover
                startButton.Text = "🔎"; // Change text on hover
                startButton.FlatStyle = FlatStyle.Popup; // Change to flat style on hover
            };
            startButton.MouseLeave += (sender, e) => {
                startButton.ForeColor = Color.WhiteSmoke; // Return to original text color
                startButton.BackColor = Color.DimGray; // Return to original
                startButton.Text = "🔍"; //  text resets to create visual effect
                startButton.FlatStyle = FlatStyle.Popup; // Reset flat style
            };

            // Connect the button to your existing transition system
            // This is where the welcome screen connects to your chat functionality
            startButton.Click += async (sender, e) => {
                string initialMessage = this.initialMessageTextBox.Text?.Trim();

                // Always transition to chat first
                TransitionToChat();

                // If there's a message, process it after transitioning
                if (!string.IsNullOrWhiteSpace(initialMessage))
                {
                    // Give the UI a moment to finish transitioning
                    await Task.Delay(200);

                    // Put the message in the chat input and process it
                    if (this.OfflineAiChatBox != null)
                    {
                        this.OfflineAiChatBox.Text = initialMessage;
                        await ProcessUserRequestAsync();
                    }
                }
            };

            // Add all visual elements to the welcome panel in the correct order
            pnlWelcome.Controls.Add(titleLabel);
            pnlWelcome.Controls.Add(subtitleLabel);
            pnlWelcome.Controls.Add(initialMessageTextBox);
            pnlWelcome.Controls.Add(startButton);

            // Position elements using a mathematical approach that works across different screen sizes
            PositionWelcomeElements(titleLabel, subtitleLabel, initialMessageTextBox, startButton);

            // Handle window resizing to maintain proper layout
            // This ensures your interface looks professional regardless of window size
            pnlWelcome.Resize += (sender, e) => {
                PositionWelcomeElements(titleLabel, subtitleLabel, initialMessageTextBox, startButton);
            };
        }
        // Separate positioning method for maintainable, reusable layout logic
        // This mathematical approach ensures consistent spacing and alignment
        private void PositionWelcomeElements(Label title, Label subtitle, TextBox messageInput, Button startBtn)
        {

            // Calculate the horizontal center of the welcome panel
            int centerX = pnlWelcome.Width / 2;
            int currentY = 60; // Starting vertical position with comfortable top margin

            // Position title at the top center with calculated spacing
            title.Location = new Point(centerX - title.Width / 2, currentY);
            currentY = title.Bottom + 20; // Add space after title

            // Position subtitle below title with proportional spacing
            subtitle.Location = new Point(centerX - subtitle.Width / 2, currentY);
            currentY = subtitle.Bottom + 30; // Add space after subtitle

            // Position message input below subtitle with generous spacing
            messageInput.Location = new Point(centerX - messageInput.Width / 2, currentY);
            currentY = messageInput.Bottom + 30; // Add space after message input

            // Position start button below message input with proportional spacing
            startBtn.Location = new Point(centerX - startBtn.Width / 2, currentY);
        }
        private void SetupChatInterface()
        {
            try
            {
                _logger.LogInfo("Setting up simple chat interface");
                pnlChat.Controls.Clear();

                // Create a simple container panel
                Panel mainContainer = new Panel
                {
                    Dock = DockStyle.Fill,
                    BackColor = FeniColors.MainBackground
                };

                // Create left sidebar (fixed width) - STORE THE REFERENCE
                _leftSidebar = new Panel
                {
                    Width = 280,
                    Dock = DockStyle.Left,
                    BackColor = FeniColors.SidebarBackground
                };

                // Create right chat area (fills remaining space)
                Panel rightChatArea = new Panel
                {
                    Dock = DockStyle.Fill,
                    BackColor = FeniColors.MainBackground
                };

                // Add a subtle divider line between panels
                Panel divider = new Panel
                {
                    Width = 1,
                    Dock = DockStyle.Left,
                    BackColor = Color.FromArgb(60, 60, 60)
                };

                // Set up the sidebar
                Panel sidebar = CreateClaudeStyleSidebar();
                _leftSidebar.Controls.Add(sidebar);

                // Set up the main chat area
                Panel chatArea = CreateMainChatArea();
                rightChatArea.Controls.Add(chatArea);

                // Add all panels to main container (order matters for docking)
                mainContainer.Controls.Add(rightChatArea);  // Fill area first
                mainContainer.Controls.Add(divider);       // Divider second
                mainContainer.Controls.Add(_leftSidebar);   // Fixed width last

                // Add main container to chat panel
                pnlChat.Controls.Add(mainContainer);

                // Reset sidebar state
                _sidebarVisible = true;

                _logger.LogInfo("Simple chat interface setup complete");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error in SetupChatInterface: {ex.Message}");
                throw;
            }
        }
        private Panel CreateClaudeStyleSidebar()
        {
            Panel sidebar = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = FeniColors.SidebarBackground,
                Padding = new Padding(0)
            };

            // Header with Feni logo
            Panel headerPanel = new Panel
            {
                Height = 60,
                Dock = DockStyle.Top,
                BackColor = FeniColors.SidebarBackground,
                Padding = new Padding(15, 15, 15, 10)
            };

            Label logoLabel = new Label
            {
                Text = "🔍 Feni",
                Font = new Font("Georgia", 14F, FontStyle.Bold),
                ForeColor = FeniColors.TextPrimary,
                AutoSize = true,
                Location = new Point(15, 18)
            };

            headerPanel.Controls.Add(logoLabel);

            // New Chat button (Claude style)
            Panel newChatPanel = new Panel
            {
                Height = 50,
                Dock = DockStyle.Top,
                BackColor = FeniColors.SidebarBackground,
                Padding = new Padding(15, 5, 15, 5)
            };

            Button newChatBtn = new Button
            {
                Text = "+ New chat",
                Dock = DockStyle.Fill,
                FlatStyle = FlatStyle.Flat,
                BackColor = FeniColors.HoverColor,
                ForeColor = FeniColors.TextPrimary,
                Font = new Font("Georgia", 10F),
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(15, 0, 0, 0),
                Cursor = Cursors.Hand
            };

            newChatBtn.FlatAppearance.BorderSize = 0;
            newChatBtn.FlatAppearance.MouseOverBackColor = Color.FromArgb(60, 60, 60);
            newChatBtn.Click += NewChatButton_Click;

            newChatPanel.Controls.Add(newChatBtn);

            // Chat history section
            Panel historyContainer = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = FeniColors.SidebarBackground,
                Padding = new Padding(0, 10, 0, 60)
            };

            // Scrollable chat list
            _chatListPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                BackColor = FeniColors.SidebarBackground,
                Padding = new Padding(10, 0, 10, 0)
            };

            // Add some sample chat items for testing
            AddSampleChatItems();

            historyContainer.Controls.Add(_chatListPanel);

            // Settings section
            Panel settingsPanel = new Panel
            {
                Height = 50,
                Dock = DockStyle.Bottom,
                BackColor = FeniColors.SidebarBackground,
                Padding = new Padding(15, 10, 15, 10)
            };

            Button settingsBtn = new Button
            {
                Text = "⚙ Settings",
                Dock = DockStyle.Fill,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.Transparent,
                ForeColor = FeniColors.TextSecondary,
                Font = new Font("Georgia", 9F),
                TextAlign = ContentAlignment.MiddleLeft,
                Cursor = Cursors.Hand
            };
            settingsBtn.FlatAppearance.BorderSize = 0;
            settingsBtn.FlatAppearance.MouseOverBackColor = FeniColors.HoverColor;
            settingsBtn.Click += (s, e) => ShowSettingsDialog();
            settingsPanel.Controls.Add(settingsBtn);

            // Add all sections to sidebar
            sidebar.Controls.Add(historyContainer);
            sidebar.Controls.Add(newChatPanel);
            sidebar.Controls.Add(headerPanel);
            sidebar.Controls.Add(settingsPanel);

            return sidebar;
        }
        private void AddSampleChatItems()
        {
            string[] sampleChats = {
        
    };

            foreach (string chatTitle in sampleChats)
            {
                Panel chatItem = CreateChatHistoryItem(chatTitle);
                _chatListPanel.Controls.Add(chatItem);
            }
        }

        private Panel CreateChatHistoryItem(string fullTitle)
        {
            // Truncate to ~30 characters like Claude
            string displayTitle = fullTitle.Length > 30 ? fullTitle.Substring(0, 30) + "..." : fullTitle;

            Panel chatItem = new Panel
            {
                Width = _chatListPanel.Width - 20,
                Height = 45,
                BackColor = Color.Transparent,
                Cursor = Cursors.Hand,
                Padding = new Padding(10, 8, 10, 8)
            };

            Label titleLabel = new Label
            {
                Text = displayTitle,
                Font = new Font("Georgia", 9F),
                ForeColor = FeniColors.TextPrimary,
                AutoSize = false,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft
            };

            // Hover effects like Claude
            chatItem.MouseEnter += (s, e) => {
                chatItem.BackColor = FeniColors.HoverColor;
            };
            chatItem.MouseLeave += (s, e) => {
                chatItem.BackColor = Color.Transparent;
            };

            titleLabel.MouseEnter += (s, e) => {
                chatItem.BackColor = FeniColors.HoverColor;
            };
            titleLabel.MouseLeave += (s, e) => {
                chatItem.BackColor = Color.Transparent;
            };

            chatItem.Controls.Add(titleLabel);
            return chatItem;
        }
        private Panel CreateMainChatArea()
        {
            Panel mainArea = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = FeniColors.MainBackground
            };

            TableLayoutPanel layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                RowCount = 3,
                ColumnCount = 1,
                BackColor = FeniColors.MainBackground
            };

            // Header (40px), Chat area (fill), Input (80px)
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 80F));

            // Header with back button
            Panel header = CreateChatHeader();
            layout.Controls.Add(header, 0, 0);

            // Chat display area
            Panel chatDisplay = CreateChatDisplayArea();
            layout.Controls.Add(chatDisplay, 0, 1);

            // Input area with your welcome screen styling
            Panel inputArea = CreateStyledInputArea();
            layout.Controls.Add(inputArea, 0, 2);

            mainArea.Controls.Add(layout);
            return mainArea;
        }

        private Panel CreateChatHeader()
        {
            Panel header = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = FeniColors.MainBackground
            };

            Button backBtn = new Button
            {
                Text = "←",
                Width = 35,
                Height = 30,
                Location = new Point(20, 5),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.Transparent,
                ForeColor = FeniColors.TextSecondary,
                Font = new Font("Georgia", 14F),
                Cursor = Cursors.Hand
            };
            backBtn.FlatAppearance.BorderSize = 0;
            backBtn.FlatAppearance.MouseOverBackColor = FeniColors.HoverColor;

            // NEW DOUBLE-CLICK LOGIC
            backBtn.Click += (s, e) => HandleBackButtonClick();

            header.Controls.Add(backBtn);
            return header;
        }
        private void HandleBackButtonClick()
        {
            var now = DateTime.Now;
            var timeSinceLastClick = (now - _lastBackClick).TotalMilliseconds;

            if (timeSinceLastClick <= DOUBLE_CLICK_TIME_MS)
            {
                // Double-click detected - go to home screen
                TransitionToWelcome();
            }
            else
            {
                // Single click - toggle sidebar
                ToggleSidebar();
            }

            _lastBackClick = now;
        }

        private void ToggleSidebar()
        {
            if (_leftSidebar == null) return;

            _sidebarVisible = !_sidebarVisible;
            _leftSidebar.Visible = _sidebarVisible;
        }

        private Panel CreateChatDisplayArea()
        {
            Panel chatPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = FeniColors.MainBackground,
                Padding = new Padding(40, 20, 40, 20)
            };

            // Initialize or reuse rtbChatDisplay
            if (rtbChatDisplay == null)
            {
                rtbChatDisplay = new RichTextBox
                {
                    Dock = DockStyle.Fill,
                    BackColor = FeniColors.MainBackground,
                    ForeColor = FeniColors.TextPrimary,
                    Font = new Font("Georgia", 12F),
                    ReadOnly = true,
                    BorderStyle = BorderStyle.None,
                    ScrollBars = RichTextBoxScrollBars.Vertical
                };
            }
            else
            {
                rtbChatDisplay.Parent?.Controls.Remove(rtbChatDisplay);
                rtbChatDisplay.Dock = DockStyle.Fill;
                rtbChatDisplay.BackColor = FeniColors.MainBackground;
                rtbChatDisplay.ForeColor = FeniColors.TextPrimary;
            }

            chatPanel.Controls.Add(rtbChatDisplay);
            return chatPanel;
        }

        private Panel CreateStyledInputArea()
        {
            Panel inputPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = FeniColors.MainBackground,
                Padding = new Padding(40, 15, 40, 15)
            };

            // Create rounded input container (simulated with background color)
            Panel inputContainer = new Panel
            {
                Height = 50,
                Dock = DockStyle.Fill,
                BackColor = FeniColors.InputBackground,
                Padding = new Padding(15, 10, 50, 10)
            };

            // Initialize or reuse input textbox
            if (OfflineAiChatBox == null)
            {
                OfflineAiChatBox = new TextBox
                {
                    Multiline = true,
                    ScrollBars = ScrollBars.None,
                    Font = new Font("Georgia", 11F),
                    ForeColor = FeniColors.TextPrimary,
                    BackColor = FeniColors.InputBackground,
                    BorderStyle = BorderStyle.None,
                    Dock = DockStyle.Fill
                };
                OfflineAiChatBox.KeyDown += OfflineAiChatBox_KeyDown;
                OfflineAiChatBox.TextChanged += OfflineAiChatBox_TextChanged;
            }
            else
            {
                OfflineAiChatBox.Parent?.Controls.Remove(OfflineAiChatBox);
                OfflineAiChatBox.Dock = DockStyle.Fill;
                OfflineAiChatBox.BackColor = FeniColors.InputBackground;
                OfflineAiChatBox.ForeColor = FeniColors.TextPrimary;
            }

            // Send button with your styling
            if (SendButton == null)
            {
                SendButton = new Button
                {
                    Text = "🔍",
                    Width = 35,
                    Height = 30,
                    FlatStyle = FlatStyle.Flat,
                    BackColor = FeniColors.AccentRed,
                    ForeColor = FeniColors.TextPrimary,
                    Font = new Font("Georgia", 11F),
                    Cursor = Cursors.Hand
                };
                SendButton.FlatAppearance.BorderSize = 0;
                SendButton.Click += SendButton_Click;
            }
            else
            {
                SendButton.Parent?.Controls.Remove(SendButton);
                SendButton.BackColor = FeniColors.AccentRed;
                SendButton.ForeColor = FeniColors.TextPrimary;
            }

            SendButton.Dock = DockStyle.Right;
            SendButton.Width = 35;

            inputContainer.Controls.Add(OfflineAiChatBox);
            inputContainer.Controls.Add(SendButton);
            inputPanel.Controls.Add(inputContainer);

            return inputPanel;
        }
        private Panel CreateSidePanel()
        {
            Panel sidePanel = new Panel
            {
                Dock = DockStyle.Fill,  // Fill the entire Panel1 of SplitContainer
                BackColor = Color.FromArgb(17, 17, 17), // Darker than main area
                Padding = new Padding(0)
            };

            // Header section
            Panel headerPanel = new Panel
            {
                Height = 70,
                Dock = DockStyle.Top,
                BackColor = Color.FromArgb(17, 17, 17),
                Padding = new Padding(15, 15, 15, 10) // Reduced padding
            };

            // Feni logo/title
            Label logoLabel = new Label
            {
                Text = "🔍 Feni",
                Font = new Font("Segoe UI", 13F, FontStyle.Bold), // Slightly smaller
                ForeColor = Color.White,
                AutoSize = true,
                Location = new Point(15, 15)
            };

            // New Chat button - make it responsive to panel width
            Button newChatBtn = new Button
            {
                Text = "+ New chat",
                Height = 35,
                Dock = DockStyle.Bottom, // Use dock instead of fixed width
                Margin = new Padding(15, 10, 15, 5),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(45, 45, 45),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9F),
                Cursor = Cursors.Hand,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(10, 0, 0, 0)
            };
            newChatBtn.FlatAppearance.BorderSize = 0;
            newChatBtn.FlatAppearance.MouseOverBackColor = Color.FromArgb(55, 55, 55);
            newChatBtn.Click += NewChatButton_Click;

            headerPanel.Controls.Add(logoLabel);
            headerPanel.Controls.Add(newChatBtn);

            // Chat history section
            Panel historySection = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(10, 10, 10, 60), // Bottom padding for settings
                BackColor = Color.FromArgb(17, 17, 17)
            };

            // Scrollable chat list
            _chatListPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                BackColor = Color.FromArgb(17, 17, 17),
                Padding = new Padding(5) // Add some padding
            };

            historySection.Controls.Add(_chatListPanel);

            // Settings section at bottom
            Panel settingsSection = CreateSettingsSection();

            // Add all sections to side panel
            sidePanel.Controls.Add(historySection);
            sidePanel.Controls.Add(settingsSection);
            sidePanel.Controls.Add(headerPanel);

            return sidePanel;
        }
        private Panel CreateChatArea()
        {
            Panel chatArea = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(32, 32, 32)
            };

            // Create main layout
            TableLayoutPanel mainLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                RowCount = 3,
                ColumnCount = 1,
                BackColor = Color.FromArgb(32, 32, 32)
            };

            // Row styles: Header (50px), Chat (Fill), Input (80px)
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50F));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 80F));

            // Header with back button
            Panel headerPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(32, 32, 32)
            };

            // Back button
            Button backBtn = new Button
            {
                Text = "←",
                Width = 40,
                Height = 40,
                Location = new Point(20, 5),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.Transparent,
                ForeColor = Color.FromArgb(150, 150, 150),
                Font = new Font("Segoe UI", 16F),
                Cursor = Cursors.Hand
            };
            backBtn.FlatAppearance.BorderSize = 0;
            backBtn.FlatAppearance.MouseOverBackColor = Color.FromArgb(45, 45, 45);
            backBtn.Click += (s, e) => {
                // Save current chat
                SaveCurrentChat();
                // Go back to welcome
                TransitionToWelcome();
            };

            headerPanel.Controls.Add(backBtn);

            // Chat display panel
            Panel chatPanel = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(20, 0, 20, 0)
            };

            // Initialize rtbChatDisplay if it doesn't exist
            if (rtbChatDisplay == null)
            {
                rtbChatDisplay = new RichTextBox
                {
                    Dock = DockStyle.Fill,
                    BackColor = Color.FromArgb(32, 32, 32),
                    ForeColor = Color.White,
                    Font = new Font("Segoe UI", 11F),
                    ReadOnly = true,
                    BorderStyle = BorderStyle.None,
                    ScrollBars = RichTextBoxScrollBars.Vertical
                };
            }
            else
            {
                // Remove from previous parent if any
                rtbChatDisplay.Parent?.Controls.Remove(rtbChatDisplay);
                rtbChatDisplay.Dock = DockStyle.Fill;
            }

            chatPanel.Controls.Add(rtbChatDisplay);

            // Input area
            Panel inputPanel = CreateModernInputArea();

            // Add all to layout
            mainLayout.Controls.Add(headerPanel, 0, 0);
            mainLayout.Controls.Add(chatPanel, 0, 1);
            mainLayout.Controls.Add(inputPanel, 0, 2);

            chatArea.Controls.Add(mainLayout);

            return chatArea;
        }
        private void NewChatButton_Click(object sender, EventArgs e)
        {
            // Clear current chat and go to welcome screen for new conversation
            if (rtbChatDisplay != null)
            {
                rtbChatDisplay.Clear(); // Clear the current conversation
            }

            // Go back to welcome screen for fresh start
            TransitionToWelcome();
        }
        private Panel CreateSettingsSection()
        {
            Panel settingsPanel = new Panel
            {
                Height = 50,
                Dock = DockStyle.Bottom,
                BackColor = Color.FromArgb(17, 17, 17),
                Padding = new Padding(15, 10, 15, 10)
            };

            Button settingsBtn = new Button
            {
                Text = "⚙ Settings",
                Dock = DockStyle.Fill,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.Transparent,
                ForeColor = Color.FromArgb(150, 150, 150),
                Font = new Font("Segoe UI", 9F),
                TextAlign = ContentAlignment.MiddleLeft,
                Cursor = Cursors.Hand
            };
            settingsBtn.FlatAppearance.BorderSize = 0;
            settingsBtn.FlatAppearance.MouseOverBackColor = Color.FromArgb(45, 45, 45);
            settingsBtn.Click += (s, e) => ShowSettingsDialog();

            settingsPanel.Controls.Add(settingsBtn);
            return settingsPanel;
        }

        private void SaveCurrentChat()
        {
            // Placeholder - we'll implement this in Step 5
        }

        private void TransitionToWelcome()
        {
            // This should already exist in your code
            // If not, add this placeholder:
            pnlChat.Visible = false;
            pnlWelcome.Visible = true;
        }

        private Panel CreateModernInputArea()
        {
            Panel inputPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(32, 32, 32),
                Padding = new Padding(20, 15, 20, 15)
            };

            // Create a container for the input controls
            Panel inputContainer = new Panel
            {
                Height = 50,
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(45, 45, 48),
                Padding = new Padding(10, 5, 50, 5) // Right padding for send button
            };

            // Initialize or reuse OfflineAiChatBox
            if (OfflineAiChatBox == null)
            {
                OfflineAiChatBox = new TextBox
                {
                    Multiline = true,
                    ScrollBars = ScrollBars.Vertical,
                    Font = new Font("Segoe UI", 11F),
                    ForeColor = Color.White,
                    BackColor = Color.FromArgb(45, 45, 48),
                    BorderStyle = BorderStyle.None,
                    Dock = DockStyle.Fill
                };
                OfflineAiChatBox.KeyDown += OfflineAiChatBox_KeyDown;
                OfflineAiChatBox.TextChanged += OfflineAiChatBox_TextChanged;
            }
            else
            {
                // Remove from previous parent if any
                OfflineAiChatBox.Parent?.Controls.Remove(OfflineAiChatBox);
                OfflineAiChatBox.Dock = DockStyle.Fill;
            }

            // Initialize or reuse SendButton
            if (SendButton == null)
            {
                SendButton = new Button
                {
                    Text = "🔎",
                    Width = 65,
                    Height = 40,
                    FlatStyle = FlatStyle.Flat,
                    BackColor = Color.FromArgb(0, 122, 255),
                    ForeColor = Color.White,
                    Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                    Cursor = Cursors.Hand
                };
                SendButton.FlatAppearance.BorderSize = 0;
                SendButton.Click += SendButton_Click;
            }
            else
            {
                // Remove from previous parent if any
                SendButton.Parent?.Controls.Remove(SendButton);
            }

            // Position send button
            SendButton.Dock = DockStyle.Right;
            SendButton.Width = 40;

            inputContainer.Controls.Add(OfflineAiChatBox);
            inputContainer.Controls.Add(SendButton);
            inputPanel.Controls.Add(inputContainer);

            return inputPanel;
        }
        private void AddStartButtonToWelcomePanel()
        {
            Button btnStart = new Button
            {
                Text = "",
                Size = new Size(180, 50),
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 12, FontStyle.Bold),
                BackColor = Color.Firebrick,
                ForeColor = Color.White
            };

            // Center the button at the bottom
            btnStart.Location = new Point(
                (pnlWelcome.ClientSize.Width - btnStart.Width) / 2,
                pnlWelcome.ClientSize.Height - btnStart.Height - 50
            );

            // Add transition event
            btnStart.Click += (s, e) => {
                // Provide immediate professional visual feedback
                if (s is Button clickedButton)
                {
                    clickedButton.Text = "";
                    clickedButton.Enabled = false;
                    clickedButton.Update(); // Ensure immediate visual update
                }

                // Execute your sophisticated transition system
                TransitionToChat();
            };

            // Add to welcome panel
            pnlWelcome.Controls.Add(btnStart);

            // Keep it centered when form resizes
            pnlWelcome.Resize += (s, e) => {
                btnStart.Location = new Point(
                    (pnlWelcome.ClientSize.Width - btnStart.Width) / 2,
                    pnlWelcome.ClientSize.Height - btnStart.Height - 50
                );
            };
        }
        private void TransitionToChat()
        {
            try
            {
                if (pnlChat?.Visible == true)
                {
                    _logger.LogInfo("Already in chat mode");
                    return;
                }

                _logger.LogInfo("Starting transition to chat mode");

                // Set up chat interface if not already done
                if (pnlChat.Controls.Count == 0)
                {
                    _logger.LogInfo("Setting up chat interface...");
                    SetupChatInterface();
                    _logger.LogInfo("Chat interface setup complete");
                }

                _logger.LogInfo("Hiding welcome panel");
                if (pnlWelcome != null)
                {
                    pnlWelcome.Visible = false;
                }

                _logger.LogInfo("Showing chat panel");
                if (pnlChat != null)
                {
                    pnlChat.Visible = true;
                    pnlChat.BringToFront();
                }

                _logger.LogInfo("Setting focus to input");
                if (OfflineAiChatBox != null)
                {
                    OfflineAiChatBox.Focus();
                    OfflineAiChatBox.Select();
                }

                _logger.LogInfo("Successfully transitioned to chat");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Transition error: {ex.Message}");
                _logger.LogError($"Stack trace: {ex.StackTrace}");

                // Show more detailed error to help us debug
                MessageBox.Show($"Error details:\n{ex.Message}\n\nInner Exception:\n{ex.InnerException?.Message}\n\nStack trace:\n{ex.StackTrace}",
                    "Debug Info", MessageBoxButtons.OK, MessageBoxIcon.Error);

                try
                {
                    if (pnlWelcome != null) pnlWelcome.Visible = true;
                    this.Text = "Feni";
                }
                catch { }
            }
        }
        private void ShowSettingsDialog()
        {
            using (var settingsForm = new Form())
            {
                // Set up the form
                settingsForm.Text = "Feni Settings";
                settingsForm.Size = new Size(400, 250);
                settingsForm.StartPosition = FormStartPosition.CenterParent;
                settingsForm.FormBorderStyle = FormBorderStyle.FixedDialog;
                settingsForm.MaximizeBox = false;
                settingsForm.MinimizeBox = false;
                settingsForm.BackColor = FeniColors.MainBackground;

                // Theme selection
                var themeLabel = new Label
                {
                    Text = "Theme:",
                    Location = new Point(20, 30),
                    ForeColor = FeniColors.TextPrimary,
                    Font = new Font("Georgia", 11F, FontStyle.Bold),
                    AutoSize = true
                };

                var themeCombo = new ComboBox
                {
                    Location = new Point(20, 55),
                    Size = new Size(150, 25),
                    DropDownStyle = ComboBoxStyle.DropDownList,
                    BackColor = FeniColors.InputBackground,
                    ForeColor = FeniColors.TextPrimary,
                    Font = new Font("Georgia", 10F)
                };
                themeCombo.Items.AddRange(new[] { "Dark Mode", "Light Mode" });

                // Load current setting
                string currentTheme = _config.GetSetting("Theme", "Dark");
                themeCombo.SelectedIndex = currentTheme == "Light" ? 1 : 0;

                // Model selection
                var modelLabel = new Label
                {
                    Text = "AI Model:",
                    Location = new Point(20, 100),
                    ForeColor = FeniColors.TextPrimary,
                    Font = new Font("Georgia", 11F, FontStyle.Bold),
                    AutoSize = true
                };

                var modelCombo = new ComboBox
                {
                    Location = new Point(20, 125),
                    Size = new Size(200, 25),
                    DropDownStyle = ComboBoxStyle.DropDownList,
                    BackColor = FeniColors.InputBackground,
                    ForeColor = FeniColors.TextPrimary,
                    Font = new Font("Georgia", 10F)
                };
                modelCombo.Items.Add("Llama 3.2 3B (Current)");
                modelCombo.SelectedIndex = 0;

                // Save button
                var saveBtn = new Button
                {
                    Text = "Save",
                    Location = new Point(200, 170),
                    Size = new Size(80, 30),
                    FlatStyle = FlatStyle.Flat,
                    BackColor = FeniColors.AccentRed,
                    ForeColor = Color.White,
                    Font = new Font("Georgia", 9F, FontStyle.Bold)
                };
                saveBtn.FlatAppearance.BorderSize = 0;
                saveBtn.Click += (s, e) => {
                    // Save settings
                    string selectedTheme = themeCombo.SelectedIndex == 1 ? "Light" : "Dark";
                    _config.SetSetting("Theme", selectedTheme);
                    _config.SetSetting("DefaultModel", "llama3.2:3B");

                    // Apply theme
                    ApplyThemeSettings();

                    settingsForm.DialogResult = DialogResult.OK;
                };

                // Cancel button
                var cancelBtn = new Button
                {
                    Text = "Cancel",
                    Location = new Point(290, 170),
                    Size = new Size(80, 30),
                    FlatStyle = FlatStyle.Flat,
                    BackColor = FeniColors.HoverColor,
                    ForeColor = FeniColors.TextPrimary,
                    Font = new Font("Georgia", 9F)
                };
                cancelBtn.FlatAppearance.BorderSize = 0;
                cancelBtn.Click += (s, e) => settingsForm.DialogResult = DialogResult.Cancel;

                // Add controls
                settingsForm.Controls.AddRange(new Control[] {
            themeLabel, themeCombo, modelLabel, modelCombo, saveBtn, cancelBtn
        });

                // Show the dialog
                settingsForm.ShowDialog(this);
            }
        }

        private void ApplyThemeSettings()
        {
            string themeSetting = _config.GetSetting("Theme", "Dark");
            FeniColors.CurrentTheme = themeSetting == "Light" ? AppTheme.Light : AppTheme.Dark;

            // Refresh the interface
            this.BackColor = FeniColors.MainBackground;
            if (pnlWelcome != null) pnlWelcome.BackColor = FeniColors.MainBackground;
            if (pnlChat != null) pnlChat.BackColor = FeniColors.MainBackground;
        }
        private void CheckOllamaInstallation()
        {
            try
            {
                using (var client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(5);
                    var response = client.GetAsync("http://localhost:11434/api/version").Result;
                    if (response.IsSuccessStatusCode)
                    {
                        _logger.LogInfo("Ollama service detected");
                        return;
                    }
                }
            }
            catch { }

            // Ollama not detected
            MessageBox.Show(
                "Ollama service not detected. Please install Ollama first.\n\n" +
                "You can download it from: https://ollama.com/download\n\n" +
                "After installation, run 'ollama serve' and 'ollama pull llama3.2:3b'",
                "Feni - Setup",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        #endregion

        #region Event Handlers

        #region Windows Form Designer generated code

        /// <summary>
        //#region Event Handlers

        private void StartConversationButton_Click(object sender, EventArgs e)
        {
            string initialMessage = this.initialMessageTextBox.Text;

            // Don't process if it's still the placeholder text or empty
            if (string.IsNullOrWhiteSpace(initialMessage) ||
                initialMessage == "")
            {
                // Transition to chat without an initial message
                TransitionToChatInterface("");
            }
            else
            {
                // Transition to chat with the user's initial message
                TransitionToChatInterface(initialMessage);
            }
        }

        private void TransitionToChatInterface(string initialMessage)
        {
            // Hide the welcome panel
            this.pnlWelcome.Visible = false;

            // Show the chat panel (your existing chat interface)
            this.pnlChat.Visible = true;

            // If there's an initial message, process it immediately:)
            if (!string.IsNullOrWhiteSpace(initialMessage))
            {
                // Add the user's message to the chat
                AddUserMessage(initialMessage);

                // Process the message with your AI
                ProcessUserMessage(initialMessage);
            }

            // Focus on the chat input for continued conversation
            this.OfflineAiChatBox.Focus();
        }

        #endregion/ Required method for Designer support - do not modify
        private void ApplyInitialTheme()
        {
            string themeSetting = _config.GetSetting("Theme", "Dark");
            FeniColors.CurrentTheme = themeSetting == "Light" ? AppTheme.Light : AppTheme.Dark;
        }
        /// the contents of this method with the code editor.
        /// </summary>
        private void Form1_Load(object sender, EventArgs e)
        {
            ApplyInitialTheme();
            // Perform runtime security checks
            CheckOllamaInstallation();
            VerifyApplicationIntegrity();
        }

        private async void ImageUploadButton_Click(object sender, EventArgs e)
        {
            await HandleFileUploadAsync(FileType.Image);
        }

        private async void DocumentUploadButton_Click(object sender, EventArgs e)
        {
            await HandleFileUploadAsync(FileType.Document);
        }

        private async void SendButton_Click(object sender, EventArgs e)
        {
            
            // First ensure we're in chat mode
            if (!pnlChat.Visible)
            {
                TransitionToChat();
                // Give time for transition
                await Task.Delay(300);
            }

            // Then process the message
            await ProcessUserRequestAsync();
        }

        private void OfflineAiChatBox_TextChanged(object sender, EventArgs e)
        {
            // Limit input length to prevent attacks
            if (OfflineAiChatBox.Text.Length > MAX_INPUT_LENGTH)
            {
                OfflineAiChatBox.Text = OfflineAiChatBox.Text.Substring(0, MAX_INPUT_LENGTH);
                OfflineAiChatBox.SelectionStart = OfflineAiChatBox.Text.Length;
                MessageBox.Show($"",
                    "", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        // Handle Enter key
        private async void OfflineAiChatBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter && !e.Shift && !_isProcessing)
            {
                e.SuppressKeyPress = true;
                await ProcessUserRequestAsync();
            }
        }
        #endregion

        #region Security Methods
        private void VerifyApplicationIntegrity()
        {
            try
            {
                // Verify critical files exist
                var appPath = Application.ExecutablePath;
                var appDir = Path.GetDirectoryName(appPath);

                // Check if running from approved location
                var allowedPath = _config.GetSetting("");
                if (!string.IsNullOrEmpty(allowedPath) && !appDir.StartsWith(allowedPath))
                {
                    _logger.LogSecurity($"");
                }

                // Check if running as Administrator (typically not needed for this app)
                if (IsRunningAsAdmin())
                {
                    _logger.LogSecurity("");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"");
            }
        }

        private bool IsRunningAsAdmin()
        {
            try
            {
                using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
                var principal = new System.Security.Principal.WindowsPrincipal(identity);
                return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }

        private string SanitizeUserInput(string input)
        {
            if (string.IsNullOrEmpty(input)) return string.Empty;

            // Remove potentially dangerous characters
            var sanitized = Regex.Replace(input, @"[^\w\s.,!?()-:;@#$%^&*]", "",
                RegexOptions.None, TimeSpan.FromMilliseconds(100));

            // Prevent overly long inputs
            if (sanitized.Length > MAX_INPUT_LENGTH)
                sanitized = sanitized.Substring(0, MAX_INPUT_LENGTH) + "";

            return sanitized;
        }

        private bool CheckRateLimit()
        {
            lock (_rateLimitLock)
            {
                var now = DateTime.UtcNow;

                // Reset count if more than a minute has passed
                if ((now - _lastRequestTime).TotalMinutes >= 1)
                {
                    _requestCount = 0;
                    _lastRequestTime = now;
                }

                // Allow max requests per minute
                if (_requestCount >= MAX_REQUESTS_PER_MINUTE)
                    return false;

                _requestCount++;
                return true;
            }
        }

        private string SanitizeAIResponse(string response)
        {
            if (string.IsNullOrEmpty(response)) return string.Empty;

            // Replace any HTML/script tags for security
            response = Regex.Replace(response, @"<script\b[^>]*>(.*?)</script>", "[script removed]",
                RegexOptions.IgnoreCase | RegexOptions.Singleline, TimeSpan.FromMilliseconds(200));

            // Replace potentially dangerous HTML
            response = Regex.Replace(response, @"<iframe\b[^>]*>(.*?)</iframe>", "[iframe removed]",
                RegexOptions.IgnoreCase | RegexOptions.Singleline, TimeSpan.FromMilliseconds(200));

            return response;
        }
        #endregion

        #region File Upload Logic
        private async Task HandleFileUploadAsync(FileType fileType)
        {
            if (!ValidateCanAddFiles())
                return;

            ConfigureFileDialog(fileType);

            if (_openFileDialog.ShowDialog() != DialogResult.OK)
                return;

            await ProcessSelectedFilesAsync(_openFileDialog.FileNames, fileType);
        }

        private bool ValidateCanAddFiles()
        {
            if (_attachedFiles.Count >= MAX_FILES_ALLOWED)
            {
                MessageBox.Show($"Maximum {MAX_FILES_ALLOWED} files allowed", "File Limit", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return false;
            }
            return true;
        }

        private void ConfigureFileDialog(FileType fileType)
        {
            switch (fileType)
            {
                case FileType.Image:
                    _openFileDialog.Filter = "Images|*.jpg;*.jpeg;*.png;*.bmp;*.gif";
                    _openFileDialog.Title = "Select Images for AI Analysis";
                    break;
                case FileType.Document:
                    _openFileDialog.Filter = "Documents|*.txt;*.md;*.cs;*.py;*.js;*.html;*.css;*.json;*.xml";
                    _openFileDialog.Title = "Select Documents for AI Analysis";
                    break;
            }
            _openFileDialog.Multiselect = true;
            _openFileDialog.CheckFileExists = true;
            _openFileDialog.RestoreDirectory = true;
        }

        private async Task ProcessSelectedFilesAsync(string[] filePaths, FileType fileType)
        {
            var successCount = 0;
            var failureCount = 0;

            foreach (string filePath in filePaths)
            {
                try
                {
                    // Verify file path is safe
                    var fullPath = Path.GetFullPath(filePath);
                    if (!File.Exists(fullPath) || Path.GetFileName(fullPath) != Path.GetFileName(filePath))
                    {
                        _logger.LogSecurity($"Potentially unsafe file path: {filePath}");
                        throw new Exception("Invalid file path");
                    }

                    var attachment = await ProcessFileAsync(fullPath, fileType);
                    if (attachment != null)
                    {
                        _attachedFiles.Add(attachment);
                        successCount++;
                        LogFileAttachment(attachment);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError($"File processing failed for {Path.GetFileName(filePath)}: {ex.Message}");
                    MessageBox.Show($"Error processing {Path.GetFileName(filePath)}: {ex.Message}", "File Processing Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    failureCount++;
                }
            }

            if (successCount > 0)
            {
                UpdateUIState();
                SuggestPromptIfEmpty(fileType);
                
            }

            if (failureCount > 0)
            {
               
            }
        }

        private async Task<FileAttachment> ProcessFileAsync(string filePath, FileType fileType)
        {
            if (!File.Exists(filePath))
                throw new Exception("File does not exist");

            var fileInfo = new FileInfo(filePath);
            var extension = fileInfo.Extension.ToLower();

            // Size validation
            if (fileInfo.Length > MAX_FILE_SIZE_BYTES)
                throw new Exception($"File too large: {FormatFileSize(fileInfo.Length)}. Maximum size: 10MB");

            // Extension validation
            var allowedImageExtensions = new[] { ".jpg", ".jpeg", ".png", ".bmp", ".gif" };
            var allowedDocumentExtensions = new[] { ".txt", ".md", ".cs", ".py", ".js", ".html", ".css", ".json", ".xml" };

            var allowedExtensions = fileType == FileType.Image ? allowedImageExtensions : allowedDocumentExtensions;
            if (!allowedExtensions.Contains(extension))
                throw new Exception($"Unsupported file type: {extension}");

            // Create secure temporary path with random name
            var tempFileName = Path.GetRandomFileName();
            var tempPath = Path.Combine(Path.GetTempPath(), tempFileName);

            try
            {
                // Create secure copy of file
                File.Copy(filePath, tempPath, true);

                // Process from secure location
                if (fileType == FileType.Image)
                {
                    var imageData = await ReadFileAsync(tempPath);
                    var base64Image = Convert.ToBase64String(imageData);

                    return new FileAttachment
                    {
                        FileName = fileInfo.Name,
                        Type = FileType.Image,
                        Content = base64Image,
                        Size = fileInfo.Length,
                        ProcessedAt = DateTime.Now
                    };
                }
                else
                {
                    var content = await ReadTextFileAsync(tempPath);

                    if (content.Length > MAX_DOCUMENT_LENGTH)
                    {
                        content = content.Substring(0, MAX_DOCUMENT_LENGTH) + "\n\n[Document truncated for processing...]";
                    }

                    return new FileAttachment
                    {
                        FileName = fileInfo.Name,
                        Type = FileType.Document,
                        Content = content,
                        Size = fileInfo.Length,
                        ProcessedAt = DateTime.Now
                    };
                }
            }
            finally
            {
                // Always clean up the temp file
                if (File.Exists(tempPath))
                {
                    try { File.Delete(tempPath); }
                    catch { /* Silent cleanup */ }
                }
            }
        }

        private async Task<byte[]> ReadFileAsync(string filePath)
        {
            using (var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read))
            {
                var buffer = new byte[fileStream.Length];
                await fileStream.ReadAsync(buffer, 0, buffer.Length);
                return buffer;
            }
        }

        private async Task<string> ReadTextFileAsync(string filePath)
        {
            using (var reader = new StreamReader(filePath, Encoding.UTF8))
            {
                return await reader.ReadToEndAsync();
            }
        }
        #endregion

        #region AI Communication
        private async Task ProcessUserRequestAsync()
        {
            var userInput = GetUserInput();

            if (string.IsNullOrWhiteSpace(userInput))
            {
                return; // Silent - no warning messages
            }

            await SendRequestToAIAsync(userInput);
        }

        private string GetUserInput()
        {
            if (OfflineAiChatBox == null) return "";

            var text = OfflineAiChatBox.Text?.Trim();

            // Just return the sanitized text - no filtering
            return SanitizeUserInput(text ?? "");
        }

        private async Task SendRequestToAIAsync(string userMessage)
        {
            // Rate limiting
            if (!CheckRateLimit())
            {
               
                return;
            }

            SetProcessingState(true);

            // Show user's message in Phoenix style
            AddMessageToChat($"{userMessage}", true);

            // Add to secure chat history
            _chatHistory.AddMessage(userMessage, true);

            try
            {
                var prompt = BuildPrompt(userMessage);
                var model = DetermineOptimalModel();
              

                // Show which model is being used
                

                var response = await SendToOllamaAsync(model, prompt);

                if (response.IsSuccess)
                {
                    _chatHistory.AddMessage(response.Content, false);

                    // Use the professional typewriter effect
                    await AddAIResponseWithTypewriter(response.Content);

                    _logger.LogInfo("");
                }
                else
                {
                    AddMessageToChat("please ensure Ollama is running", false);
                    _logger.LogError($"");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"");
                AddMessageToChat(" - check Ollama", false);
            }
            finally
            {
                SetProcessingState(false);
                ClearInputAndAttachments();
            }
        }

        private string BuildPrompt(string userMessage)
        {
            var promptBuilder = new StringBuilder();

            // Add current message only - no history from previous sessions
            promptBuilder.AppendLine(userMessage ?? string.Empty);

            // Add document attachments if any
            var documents = _attachedFiles.Where(f => f.Type == FileType.Document).ToList();
            if (documents.Any())
            {
                promptBuilder.AppendLine("\n\nAttached Documents:");
                for (int i = 0; i < documents.Count; i++)
                {
                    promptBuilder.AppendLine($"\n--- Document {i + 1}: {documents[i].FileName} ---");
                    promptBuilder.AppendLine(documents[i].Content);
                }
            }
            return promptBuilder.ToString();
        }

        private string DetermineOptimalModel()
        {
            return MODEL_GENERAL; //always use Phi3 model for now
        }

        private async Task<AIResponse> SendToOllamaAsync(string model, string prompt)

        {
            try
            {
                // Build JSON manually for simplicity
                var jsonBuilder = new StringBuilder();
                jsonBuilder.Append("{");
                jsonBuilder.Append($"\"model\": \"{model}\",");
                jsonBuilder.Append($"\"prompt\": \"{EscapeJsonString(prompt)}\",");
                jsonBuilder.Append("\"stream\": false,");
                jsonBuilder.Append("\"options\": {");
                jsonBuilder.Append("\"temperature\": 0.7,");
                jsonBuilder.Append("\"top_p\": 0.9");
                jsonBuilder.Append("}");

              

                jsonBuilder.Append("}");

                var jsonContent = jsonBuilder.ToString();
                var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                // Add request timeout
                using var cts = new CancellationTokenSource();
                cts.CancelAfter(TimeSpan.FromMinutes(2)); // 2-minute timeout

                var response = await _httpClient.PostAsync(OLLAMA_GENERATE_ENDPOINT, content, cts.Token);

                if (response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync();

                    // Extract response from JSON manually
                    var responseValue = ExtractResponseFromJson(responseContent);
                    if (!string.IsNullOrEmpty(responseValue))
                    {
                        // Sanitize AI response for additional security
                        var sanitizedResponse = SanitizeAIResponse(responseValue);

                        return new AIResponse { IsSuccess = true, Content = sanitizedResponse };
                    }
                    else
                    {
                        return new AIResponse { IsSuccess = false, ErrorMessage = "Invalid response format" };
                    }
                }
                else
                {
                    return new AIResponse
                    {
                        IsSuccess = false,
                        ErrorMessage = "HTTP " + response.StatusCode + ": " + response.ReasonPhrase
                    };
                }
            }
            catch (OperationCanceledException)
            {
                return new AIResponse
                {
                    IsSuccess = false,
                    ErrorMessage = "Request timed out. The operation took too long to complete."
                };
            }
            catch (HttpRequestException ex)
            {
                return new AIResponse
                {
                    IsSuccess = false,
                    ErrorMessage = "Cannot connect to Ollama. Is it running?"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError($"API request error: {ex.Message}");
                return new AIResponse
                {
                    IsSuccess = false,
                    ErrorMessage = "An error occurred while processing your request."
                };
            }
        }

        // Helper for manual JSON parsing
        private string ExtractResponseFromJson(string json)
        {
            try
            {
                var responsePattern = "\"response\":\"";
                var startIndex = json.IndexOf(responsePattern);
                if (startIndex < 0) return null;

                startIndex += responsePattern.Length;
                var endIndex = startIndex;
                var escapeNext = false;

                // Find the end of the string, handling escaped quotes
                while (endIndex < json.Length)
                {
                    var currentChar = json[endIndex];
                    if (escapeNext)
                    {
                        escapeNext = false;
                    }
                    else if (currentChar == '\\')
                    {
                        escapeNext = true;
                    }
                    else if (currentChar == '"')
                    {
                        break; // Found the end of the string
                    }
                    endIndex++;
                }

                if (endIndex > startIndex && endIndex < json.Length)
                {
                    var response = json.Substring(startIndex, endIndex - startIndex);
                    // Unescape common JSON escape sequences
                    response = response.Replace("\\\"", "\"");
                    response = response.Replace("\\\\", "\\");
                    response = response.Replace("\\n", "\n");
                    response = response.Replace("\\r", "\r");
                    response = response.Replace("\\t", "\t");
                    return response;
                }
            }
            catch
            {
                // If parsing fails, return null
            }

            return null;
        }

        // Helper for escaping JSON strings
        private string EscapeJsonString(string str)
        {
            if (string.IsNullOrEmpty(str)) return "";

            return str.Replace("\\", "\\\\")
                     .Replace("\"", "\\\"")
                     .Replace("\n", "\\n")
                     .Replace("\r", "\\r")
                     .Replace("\t", "\\t");
        }
        #endregion

        #region UI Management
        private void SetProcessingState(bool processing)
        {
            _isProcessing = processing;

            if (processing)
            {
                if (SendButton != null)
                {
                    SendButton.Enabled = false;
                    SendButton.Text = "...";
                    SendButton.ForeColor = Color.WhiteSmoke;
                }
                if (OfflineAiChatBox != null)
                {
                    OfflineAiChatBox.Enabled = false;
                }
                this.Text = "Feni -🔎...";
            }
            else
            {
                if (SendButton != null)
                {
                    SendButton.Enabled = true;
                    SendButton.Text = "🔍";
                    SendButton.ForeColor = Color.White;
                }
                if (OfflineAiChatBox != null)
                {
                    OfflineAiChatBox.Enabled = true;
                    OfflineAiChatBox.Focus();
                }
                this.Text = "Feni";
            }
        }

        private void UpdateUIState()
        {
            int imageCount = _attachedFiles.Count(f => f.Type == FileType.Image);
            int docCount = _attachedFiles.Count(f => f.Type == FileType.Document);

            // Update your button text to show file counts
            if (ImageUploadButton != null)
                ImageUploadButton.Text = imageCount > 0 ? $"🖼️({imageCount})" : "🖼️";

            if (DocumentUploadButton != null)
                DocumentUploadButton.Text = docCount > 0 ? $"📄({docCount})" : "📄";
        }

        private void SuggestPromptIfEmpty(FileType fileType)
        {
            if (OfflineAiChatBox == null || !string.IsNullOrWhiteSpace(GetUserInput()))
                return;

            var suggestion = "";
            switch (fileType)
            {
                case FileType.Image:
                    suggestion = "";
                    break;
                case FileType.Document:
                    suggestion = "";
                    break;
            }

            if (!string.IsNullOrEmpty(suggestion))
            {
              
                OfflineAiChatBox.ForeColor = Color.WhiteSmoke;
                OfflineAiChatBox.Text = suggestion;
            }
        }

        private void AddMessageToChat(string message, bool isUser)
        {
            try
            {
                if (rtbChatDisplay == null || string.IsNullOrEmpty(message)) return;

                rtbChatDisplay.SelectionStart = rtbChatDisplay.TextLength;
                rtbChatDisplay.SelectionLength = 0;

                // Subtle color difference, no timestamps
                rtbChatDisplay.SelectionColor = isUser ? Color.FromArgb(200, 200, 200) : Color.WhiteSmoke;
                rtbChatDisplay.SelectionFont = new Font("Georgia", 11F, FontStyle.Bold);
                rtbChatDisplay.AppendText($"{message}\n\n");

                // Only scroll if user is already at bottom
                if (IsNearBottom())
                {
                    rtbChatDisplay.SelectionStart = rtbChatDisplay.TextLength;
                    rtbChatDisplay.ScrollToCaret();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error displaying message: {ex.Message}");
            }
        }
        private async Task AddAIResponseWithTypewriter(string message)
        {
            if (rtbChatDisplay == null || string.IsNullOrEmpty(message)) return;

            try
            {
                // Set up formatting
                rtbChatDisplay.SelectionStart = rtbChatDisplay.TextLength;
                rtbChatDisplay.SelectionColor = Color.White;
                rtbChatDisplay.SelectionFont = new Font("Segoe UI", 12F, FontStyle.Regular);

                // For now, let's just do instant display to get it working
                rtbChatDisplay.AppendText(message + "\n\n");

                // Scroll to bottom only if user was already there
                rtbChatDisplay.SelectionStart = rtbChatDisplay.TextLength;
                rtbChatDisplay.ScrollToCaret();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Display error: {ex.Message}");
                rtbChatDisplay.AppendText($"{message}\n\n");
            }
        }
        private async Task AddAIResponseWithProfessionalDisplay(string message)
        {
            if (rtbChatDisplay == null || string.IsNullOrEmpty(message)) return;

            try
            {
                // Remember scroll position
                int scrollPos = rtbChatDisplay.GetPositionFromCharIndex(0).Y;
                bool wasAtBottom = IsNearBottom();

                // Add the complete message instantly
                rtbChatDisplay.SelectionStart = rtbChatDisplay.TextLength;
                rtbChatDisplay.SelectionColor = Color.FromArgb(100, 255, 255, 255); // Start semi-transparent
                rtbChatDisplay.SelectionFont = new Font("Segoe UI", 12F, FontStyle.Regular);

                int startIndex = rtbChatDisplay.TextLength;
                rtbChatDisplay.AppendText(message + "\n\n");
                int endIndex = rtbChatDisplay.TextLength;

                // Only scroll if user was at bottom
                if (wasAtBottom)
                {
                    rtbChatDisplay.SelectionStart = rtbChatDisplay.TextLength;
                    rtbChatDisplay.ScrollToCaret();
                }

                // Fade in effect (much cleaner than typewriter with RichTextBox)
                for (int alpha = 100; alpha <= 255; alpha += 15)
                {
                    rtbChatDisplay.Select(startIndex, endIndex - startIndex);
                    rtbChatDisplay.SelectionColor = Color.FromArgb(alpha, 255, 255, 255);
                    await Task.Delay(30);
                    Application.DoEvents();
                }

                // Ensure final color is fully opaque
                rtbChatDisplay.Select(startIndex, endIndex - startIndex);
                rtbChatDisplay.SelectionColor = Color.White;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Display error: {ex.Message}");
                rtbChatDisplay.AppendText($"{message}\n\n");
            }
        }


        // Helper to check if user is near bottom (within 50 pixels)
        private bool IsNearBottom()
        {
            if (rtbChatDisplay == null) return true;

            Point bottomPoint = new Point(1, rtbChatDisplay.ClientSize.Height - 1);
            int bottomIndex = rtbChatDisplay.GetCharIndexFromPosition(bottomPoint);
            int lastIndex = rtbChatDisplay.TextLength;

            return (lastIndex - bottomIndex) < 100;
        }

        // Helper method to check if user is scrolled to bottom
        private bool IsUserAtBottom()
        {
            if (rtbChatDisplay == null) return true;

            // Get the current scroll position
            int visibleLines = rtbChatDisplay.ClientSize.Height / rtbChatDisplay.Font.Height;
            int totalLines = rtbChatDisplay.Lines.Length;
            int topLine = rtbChatDisplay.GetLineFromCharIndex(rtbChatDisplay.GetCharIndexFromPosition(new Point(0, 0)));

            // Consider "at bottom" if within 3 lines of the actual bottom
            return (topLine + visibleLines + 3) >= totalLines;
        }
        #region Professional Event Handlers

        private void AddUserMessage(string message)
        {
            // Clean professional message display for user messages
            AddMessageToChat(message, true);
        }

        private void ProcessUserMessage(string message)
        {
            // Process with phi3 only - no model selection complexity
            Task.Run(async () => await SendRequestToAIAsync(message));
        }

        #endregion

        private void ClearInputAndAttachments()
        {
            try
            {
                if (OfflineAiChatBox != null)
                {
                    OfflineAiChatBox.ForeColor = Color.WhiteSmoke;
                    OfflineAiChatBox.Clear();
                    OfflineAiChatBox.Focus();

                    // Use BeginInvoke to ensure focus happens after all processing
                    this.BeginInvoke(new Action(() => {
                        OfflineAiChatBox.Focus();
                        OfflineAiChatBox.Select();
                    }));
                }

                _attachedFiles.Clear();
                _logger.LogInfo("Ready for next message");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to clear input: {ex.Message}");
            }
        }
        #endregion

        #region Helper Methods
        private void LogFileAttachment(FileAttachment attachment)
        {
         
        }

        private bool IsCodeFile(string fileName)
        {
            var codeExtensions = new[] { ".cs", ".py", ".js", ".html", ".css", ".json", ".xml" };
            return codeExtensions.Contains(Path.GetExtension(fileName).ToLower());
        }

        private string FormatFileSize(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024:F1} KB";
            return $"{bytes / (1024 * 1024):F1} MB";
        }
        #endregion

        #region Cleanup
        // Cleanup method - called automatically when form closes
        private void CleanupResources()
        {
            try
            {
                if (_httpClient != null)
                    _httpClient.Dispose();
                if (_openFileDialog != null)
                    _openFileDialog.Dispose();
                if (_logger != null)
                    _logger.Dispose();
                if (rtbChatDisplay != null)
                    rtbChatDisplay.Dispose();
                if (pnlChat != null)
                    pnlChat.Dispose();
                if (_chatHistory != null)
                    _chatHistory.Dispose();
            }
            catch
            {
                // Silent cleanup
            }
        }

        // Override FormClosed instead of Dispose to avoid conflicts
        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            CleanupResources();
            base.OnFormClosed(e);
        }
        #endregion

        private void PhoenixLabelBox_Click(object sender, EventArgs e)
        {

        }
    }
    
    #region Supporting Classes
    // Message class for our enhanced chat interface
    public class ChatMessage
    {
        // Core content properties that define what each message contains
        public string Content { get; set; } = "";
        public bool IsFromUser { get; set; } // true for user messages, false for AI responses
        public DateTime Timestamp { get; set; } = DateTime.Now;

        // Visual properties that control how each message appears
        public Color BackgroundColor { get; set; }
        public Color TextColor { get; set; }
        public Font MessageFont { get; set; }

        // Layout properties that control spacing and positioning
        public Padding MessagePadding { get; set; }
        public int MaxWidth { get; set; }

        // The actual visual control that displays this message
        public Panel MessageContainer { get; set; }

        // Constructor that sets up appropriate defaults
        public ChatMessage(string content, bool isFromUser)
        {
            Content = content;
            IsFromUser = isFromUser;

            if (isFromUser)
            {
                // User messages: clean, subtle styling
                BackgroundColor = Color.FromArgb(55, 65, 81);
                TextColor = Color.WhiteSmoke;
                MessageFont = new Font("Georgia", 12F, FontStyle.Bold);
                MessagePadding = new Padding(16, 12, 16, 12);
                MaxWidth = 500;
            }
            else
            {
                // AI messages: slightly different styling
                BackgroundColor = Color.FromArgb(55, 65, 81);
                TextColor = Color.WhiteSmoke;
                MessageFont = new Font("Georgia", 12F, FontStyle.Bold);
                MessagePadding = new Padding(20, 16, 20, 16);
                MaxWidth = 600;
            }
        }

        public override string ToString()
        {
            var sender = IsFromUser ? "User" : "AI";
            var preview = Content.Length > 50 ? Content.Substring(0, 50) + "..." : Content;
            return $"[{Timestamp:HH:mm:ss}] {sender}: {preview}";
        }
    }

    public class FileAttachment
    {
        public string FileName { get; set; }
        public FileType Type { get; set; }
        public string Content { get; set; }
        public long Size { get; set; }
        public DateTime ProcessedAt { get; set; }
    }

    public class AIResponse
    {
        public bool IsSuccess { get; set; }
        public string Content { get; set; }
        public string ErrorMessage { get; set; }
    }

    public class SecureLogger : IDisposable
    {
        private readonly string _logFilePath;
        private readonly object _lockObject = new object();

        public SecureLogger()
        {
            try
            {
                var logDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PhoenixOfflineAI", "Logs");
                Directory.CreateDirectory(logDirectory);
                _logFilePath = Path.Combine(logDirectory);                 
            }
            catch
            {
                // Silent fail if can't create log
            }
        }

        public void LogInfo(string message)
        {
            LogMessage("INFO", message);
        }

        public void LogError(string message)
        {
            LogMessage("ERROR", message);
        }

        public void LogSecurity(string message)
        {
            LogMessage("SECURITY", message);
        }

        private void LogMessage(string level, string message)
        {
            try
            {
                if (string.IsNullOrEmpty(_logFilePath)) return;

                lock (_lockObject)
                {
                    var logEntry = $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] [{level}] {message}{Environment.NewLine}";
                    File.AppendAllText(_logFilePath, logEntry);
                }
            }
            catch
            {
                // Silent fail - don't let logging errors crash the app
            }
        }

        public void Dispose()
        {
            // Any cleanup needed
        }
    }

    public class SecureAppConfig
    {
        private readonly string _configFilePath;
        private readonly Dictionary<string, string> _settings;

        public SecureAppConfig()
        {
            _settings = new Dictionary<string, string>();

            try
            {
                var configDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PhoenixOfflineAI");
                Directory.CreateDirectory(configDirectory);
                _configFilePath = Path.Combine(configDirectory, "settings.dat");

                LoadSettings();
            }
            catch
            {
                // Fallback to in-memory only settings if file operations fail
            }
        }

        public string GetSetting(string key, string defaultValue = "")
        {
            if (_settings.TryGetValue(key, out string value))
                return value;

            return defaultValue;
        }

        public void SetSetting(string key, string value)
        {
            _settings[key] = value;
            SaveSettings();
        }

        private void LoadSettings()
        {
            _settings.Clear();

            if (!File.Exists(_configFilePath))
                return;

            try
            {
                string[] lines = File.ReadAllLines(_configFilePath);
                foreach (var line in lines)
                {
                    var parts = line.Split('=');
                    if (parts.Length == 2)
                    {
                        _settings[parts[0]] = parts[1];
                    }
                }
            }
            catch
            {
                // If loading fails, continue with empty settings
            }
        }

        private void SaveSettings()
        {
            try
            {
                List<string> lines = new List<string>();
                foreach (var kvp in _settings)
                {
                    lines.Add($"{kvp.Key}={kvp.Value}");
                }
                File.WriteAllLines(_configFilePath, lines);
            }
            catch
            {
                // Silent fail - don't crash on save errors
            }
        }
    }

    public class SecureChatHistory : IDisposable
    {
        private readonly string _historyFilePath;
        private readonly List<ChatMessage> _messages;
        private readonly object _lockObject = new object();
        private readonly int _maxHistorySize = 1000;
        private readonly SecureLogger _logger;
        private readonly SecureAppConfig _config;

        public SecureChatHistory(SecureLogger logger, SecureAppConfig config)
        {
            _logger = logger;
            _config = config;
            _messages = new List<ChatMessage>();

            try
            {
                var historyDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PhoenixOfflineAI");
                Directory.CreateDirectory(historyDirectory);
                _historyFilePath = Path.Combine(historyDirectory, "chat_history.txt");

                LoadHistory();
            }
            catch (Exception ex)
            {
                _logger.LogError($"");
            }
        }

        public IReadOnlyList<ChatMessage> GetRecentMessages(int count)
        {
            lock (_lockObject)
            {
                return _messages.Skip(Math.Max(0, _messages.Count - count)).ToList();
            }
        }

        public void AddMessage(string content, bool isUser)
        {
            lock (_lockObject)
            {
                if (_messages.Count >= _maxHistorySize)
                {
                    _messages.RemoveAt(0);
                }

                _messages.Add(new ChatMessage(content, isUser));
                SaveHistory();
            }
        }

        public void ClearHistory()
        {
            lock (_lockObject)
            {
                _messages.Clear();
                SaveHistory();
                _logger.LogInfo("");
            }
        }

        private void LoadHistory()
        {
            // Simplified for recovery - implement full loading later if needed
        }

        private void SaveHistory()
        {
            // Simplified for recovery - implement full saving later if needed
        }

        public void Dispose()
        {
            // Any cleanup needed
        }
    }

    public enum FileType
    {
        Image,
        Document
    }
    #endregion
}
