namespace WE_ImageToPixelart
{
    partial class ColorPicker
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

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.TypeBlockID = new System.Windows.Forms.TextBox();
            this.Confirm = new System.Windows.Forms.Button();
            this.SuspendLayout();
            //
            // TypeBlockID
            //
            this.TypeBlockID.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(127)))), ((int)(((byte)(127)))), ((int)(((byte)(127)))));
            this.TypeBlockID.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.TypeBlockID.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Italic, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.TypeBlockID.ForeColor = System.Drawing.Color.Snow;
            this.TypeBlockID.Location = new System.Drawing.Point(12, 12);
            this.TypeBlockID.Name = "TypeBlockID";
            this.TypeBlockID.Size = new System.Drawing.Size(408, 20);
            this.TypeBlockID.TabIndex = 0;
            this.TypeBlockID.Text = "Type a block id..";
            this.TypeBlockID.Enter += new System.EventHandler(this.TypeBlockID_Enter);
            this.TypeBlockID.KeyPress += new System.Windows.Forms.KeyPressEventHandler(this.TypeBlockID_KeyPress);
            //
            // Confirm
            //
            this.Confirm.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(127)))), ((int)(((byte)(127)))), ((int)(((byte)(127)))));
            this.Confirm.FlatAppearance.BorderSize = 0;
            this.Confirm.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.Confirm.ForeColor = System.Drawing.Color.Snow;
            this.Confirm.Location = new System.Drawing.Point(426, 10);
            this.Confirm.Name = "Confirm";
            this.Confirm.Size = new System.Drawing.Size(75, 23);
            this.Confirm.TabIndex = 1;
            this.Confirm.Text = "Confirm";
            this.Confirm.UseVisualStyleBackColor = false;
            this.Confirm.Click += new System.EventHandler(this.Confirm_Click);
            //
            // ColorPicker
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(96F, 96F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Dpi;
            this.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(50)))), ((int)(((byte)(51)))), ((int)(((byte)(51)))));
            this.ClientSize = new System.Drawing.Size(510, 45);
            this.Controls.Add(this.Confirm);
            this.Controls.Add(this.TypeBlockID);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.Name = "ColorPicker";
            this.Text = "Block ID Picker!";
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.TextBox TypeBlockID;
        private System.Windows.Forms.Button Confirm;
    }
}