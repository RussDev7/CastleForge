/*
Copyright (c) 2025 RussDev7

This source is subject to the GNU General Public License v3.0 (GPLv3).
See https://www.gnu.org/licenses/gpl-3.0.html.

THIS PROGRAM IS FREE SOFTWARE: YOU CAN REDISTRIBUTE IT AND/OR MODIFY
IT UNDER THE TERMS OF THE GNU GENERAL PUBLIC LICENSE AS PUBLISHED BY
THE FREE SOFTWARE FOUNDATION, EITHER VERSION 3 OF THE LICENSE, OR
(AT YOUR OPTION) ANY LATER VERSION.

THIS PROGRAM IS DISTRIBUTED IN THE HOPE THAT IT WILL BE USEFUL,
BUT WITHOUT ANY WARRANTY; WITHOUT EVEN THE IMPLIED WARRANTY OF
MERCHANTABILITY OR FITNESS FOR A PARTICULAR PURPOSE. SEE THE
GNU GENERAL PUBLIC LICENSE FOR MORE DETAILS.
*/

using System.Windows.Forms;
using System.Drawing;
using System;

namespace WE_ImageToPixelart
{
    public partial class ColorPicker : Form
    {
        public ColorPicker()
        {
            InitializeComponent();
            Confirm.DialogResult = DialogResult.OK;

            // Define control tooltips.
            #region Tooltips

            // Create a new tooltip.
            ToolTip toolTip = new ToolTip()
            {
                AutoPopDelay = 5000,
                InitialDelay = 750
            };

            // Set tool texts.
            toolTip.SetToolTip(TypeBlockID, "Type a numerical block id value here.");
            toolTip.SetToolTip(Confirm, "Confirm the block id. Press the 'X' to cancel.");

            #endregion
        }

        #region Basic Configuration Controls

        // Method to return the textbox data.
        public string GetText()
        {
            return TypeBlockID.Text;
        }

        // Ensure the value is valid before passing value to main form.
        private void Confirm_Click(object sender, EventArgs e)
        {
            if (TypeBlockID.Text == "")
            {
                MessageBox.Show("ERROR: Text cannot be blank!");
            }
            else if (TypeBlockID.Text == "Type a block id..")
            {
                MessageBox.Show("ERROR: Please type a number!");
            }
            else
            {
                Confirm.DialogResult = DialogResult.OK;
            }
        }

        // Only allow numerical values in the textbox.
        private void TypeBlockID_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar))
            {
                e.Handled = true;
            }
        }

        // Clear textbox when entering.
        private void TypeBlockID_Enter(object sender, EventArgs e)
        {
            if (TypeBlockID.Text == "Type a block id..")
            {
                TypeBlockID.Text = "";
                TypeBlockID.Font = new Font(TypeBlockID.Font, FontStyle.Regular | FontStyle.Regular);
            }
        }
        #endregion
    }
}