namespace PhoenixOfflineAI
{
    partial class PhoenixOfflineUi
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }


        private void InitializeComponent()
        {
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(PhoenixOfflineUi));
            PhoenixPictureBox = new Panel();
            PhoenixLabelBox = new Label();
            SuspendLayout();
            // 
            // PhoenixPictureBox
            // 
            PhoenixPictureBox.BackColor = SystemColors.ActiveCaptionText;
            PhoenixPictureBox.BackgroundImageLayout = ImageLayout.Center;
            PhoenixPictureBox.Location = new Point(5, 2);
            PhoenixPictureBox.Name = "PhoenixPictureBox";
            PhoenixPictureBox.Size = new Size(698, 707);
            PhoenixPictureBox.TabIndex = 4;
            // 
            // PhoenixLabelBox
            // 
            PhoenixLabelBox.AutoSize = true;
            PhoenixLabelBox.Font = new Font("Tahoma", 9.75F, FontStyle.Bold, GraphicsUnit.Point, 0);
            PhoenixLabelBox.ForeColor = Color.Firebrick;
            PhoenixLabelBox.Location = new Point(8, 7);
            PhoenixLabelBox.Name = "PhoenixLabelBox";
            PhoenixLabelBox.Size = new Size(168, 16);
            PhoenixLabelBox.TabIndex = 4;
            PhoenixLabelBox.Click += PhoenixLabelBox_Click;
            // 
            // PhoenixOfflineUi
            // 
            AutoScaleDimensions = new SizeF(8F, 16F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = SystemColors.MenuText;
            ClientSize = new Size(514, 537);
            Controls.Add(PhoenixPictureBox);
            Font = new Font("Segoe UI Emoji", 9F, FontStyle.Bold | FontStyle.Italic, GraphicsUnit.Point, 0);
            ForeColor = Color.FromArgb(64, 64, 64);
            Icon = (System.Drawing.Icon)resources.GetObject("$this.Icon");
            Margin = new Padding(3, 2, 3, 2);
            MinimumSize = new Size(530, 0);
            Name = "PhoenixOfflineUi";
            Text = "Feni";
            Load += Form1_Load;
            ResumeLayout(false);
        }




        private Panel PhoenixPictureBox;
        private Label PhoenixLabelBox;
    }
}