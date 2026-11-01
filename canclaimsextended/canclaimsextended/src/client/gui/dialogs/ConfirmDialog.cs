using System;

namespace canclaimsextended.src.client.gui.dialogs
{
    /// <summary>Question plus a confirm/decline pair.</summary>
    public sealed class ConfirmDialog : GuiDialogPanel
    {
        private readonly string question;
        private readonly Action onConfirm;

        /// <param name="question">Already translated.</param>
        public ConfirmDialog(string question, Action onConfirm)
        {
            this.question = question;
            this.onConfirm = onConfirm;
        }

        protected override void BuildContent(DialogLayout l)
        {
            Text(l, question);
            ConfirmDeclineRow(l, () =>
            {
                onConfirm();
                Close();
                return true;
            });
        }
    }
}
