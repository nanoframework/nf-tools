namespace GitHubDcoCheck
{
    partial class Main
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
            this.textBoxUserName = new System.Windows.Forms.TextBox();
            this.textBoxRepository = new System.Windows.Forms.TextBox();
            this.label1 = new System.Windows.Forms.Label();
            this.label2 = new System.Windows.Forms.Label();
            this.buttonPullRequests = new System.Windows.Forms.Button();
            this.listViewPullRequests = new System.Windows.Forms.ListView();
            this.columnHeaderID = ((System.Windows.Forms.ColumnHeader)(new System.Windows.Forms.ColumnHeader()));
            this.columnHeaderDesc = ((System.Windows.Forms.ColumnHeader)(new System.Windows.Forms.ColumnHeader()));
            this.columnHeaderState = ((System.Windows.Forms.ColumnHeader)(new System.Windows.Forms.ColumnHeader()));
            this.textBoxOwner = new System.Windows.Forms.TextBox();
            this.label3 = new System.Windows.Forms.Label();
            this.listViewCommits = new System.Windows.Forms.ListView();
            this.columnHeaderSha = ((System.Windows.Forms.ColumnHeader)(new System.Windows.Forms.ColumnHeader()));
            this.columnHeaderAuthorName = ((System.Windows.Forms.ColumnHeader)(new System.Windows.Forms.ColumnHeader()));
            this.columnHeaderEmail = ((System.Windows.Forms.ColumnHeader)(new System.Windows.Forms.ColumnHeader()));
            this.label4 = new System.Windows.Forms.Label();
            this.comboBoxStatus = new System.Windows.Forms.ComboBox();
            this.buttonSet = new System.Windows.Forms.Button();
            this.label5 = new System.Windows.Forms.Label();
            this.textBoxMessage = new System.Windows.Forms.TextBox();
            this.SuspendLayout();
            // 
            // textBoxUserName
            // 
            this.textBoxUserName.Location = new System.Drawing.Point(84, 13);
            this.textBoxUserName.Name = "textBoxUserName";
            this.textBoxUserName.Size = new System.Drawing.Size(215, 21);
            this.textBoxUserName.TabIndex = 9;
            // 
            // textBoxRepository
            // 
            this.textBoxRepository.Location = new System.Drawing.Point(84, 67);
            this.textBoxRepository.Name = "textBoxRepository";
            this.textBoxRepository.Size = new System.Drawing.Size(215, 21);
            this.textBoxRepository.TabIndex = 13;
            this.textBoxRepository.Text = "nf-interpreter";
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(12, 70);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(63, 13);
            this.label1.TabIndex = 12;
            this.label1.Text = "Repository:";
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.Location = new System.Drawing.Point(12, 16);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(59, 13);
            this.label2.TabIndex = 8;
            this.label2.Text = "Username:";
            // 
            // buttonPullRequests
            // 
            this.buttonPullRequests.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.buttonPullRequests.Location = new System.Drawing.Point(466, 65);
            this.buttonPullRequests.Name = "buttonPullRequests";
            this.buttonPullRequests.Size = new System.Drawing.Size(97, 23);
            this.buttonPullRequests.TabIndex = 0;
            this.buttonPullRequests.Text = "Pull Requests";
            this.buttonPullRequests.UseVisualStyleBackColor = true;
            this.buttonPullRequests.Click += new System.EventHandler(this.buttonPullRequests_Click);
            // 
            // listViewPullRequests
            // 
            this.listViewPullRequests.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.listViewPullRequests.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] {
            this.columnHeaderID,
            this.columnHeaderDesc,
            this.columnHeaderState});
            this.listViewPullRequests.FullRowSelect = true;
            this.listViewPullRequests.GridLines = true;
            this.listViewPullRequests.Location = new System.Drawing.Point(15, 94);
            this.listViewPullRequests.Name = "listViewPullRequests";
            this.listViewPullRequests.Size = new System.Drawing.Size(548, 122);
            this.listViewPullRequests.TabIndex = 1;
            this.listViewPullRequests.UseCompatibleStateImageBehavior = false;
            this.listViewPullRequests.View = System.Windows.Forms.View.Details;
            this.listViewPullRequests.SelectedIndexChanged += new System.EventHandler(this.listViewPullRequests_SelectedIndexChanged);
            // 
            // columnHeaderID
            // 
            this.columnHeaderID.Text = "ID";
            // 
            // columnHeaderDesc
            // 
            this.columnHeaderDesc.Text = "Title";
            this.columnHeaderDesc.Width = 365;
            // 
            // columnHeaderState
            // 
            this.columnHeaderState.Text = "State";
            this.columnHeaderState.Width = 79;
            // 
            // textBoxOwner
            // 
            this.textBoxOwner.Location = new System.Drawing.Point(84, 40);
            this.textBoxOwner.Name = "textBoxOwner";
            this.textBoxOwner.Size = new System.Drawing.Size(215, 21);
            this.textBoxOwner.TabIndex = 11;
            this.textBoxOwner.Text = "nanoframework";
            // 
            // label3
            // 
            this.label3.AutoSize = true;
            this.label3.Location = new System.Drawing.Point(12, 43);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(43, 13);
            this.label3.TabIndex = 10;
            this.label3.Text = "Owner:";
            // 
            // listViewCommits
            // 
            this.listViewCommits.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] {
            this.columnHeaderSha,
            this.columnHeaderAuthorName,
            this.columnHeaderEmail});
            this.listViewCommits.FullRowSelect = true;
            this.listViewCommits.GridLines = true;
            this.listViewCommits.Location = new System.Drawing.Point(15, 248);
            this.listViewCommits.Name = "listViewCommits";
            this.listViewCommits.Size = new System.Drawing.Size(548, 157);
            this.listViewCommits.TabIndex = 3;
            this.listViewCommits.UseCompatibleStateImageBehavior = false;
            this.listViewCommits.View = System.Windows.Forms.View.Details;
            this.listViewCommits.SelectedIndexChanged += new System.EventHandler(this.listViewCommits_SelectedIndexChanged);
            // 
            // columnHeaderSha
            // 
            this.columnHeaderSha.Text = "SHA";
            this.columnHeaderSha.Width = 250;
            // 
            // columnHeaderAuthorName
            // 
            this.columnHeaderAuthorName.Text = "Author";
            this.columnHeaderAuthorName.Width = 120;
            // 
            // columnHeaderEmail
            // 
            this.columnHeaderEmail.Text = "Email";
            this.columnHeaderEmail.Width = 150;
            // 
            // label4
            // 
            this.label4.AutoSize = true;
            this.label4.Location = new System.Drawing.Point(15, 232);
            this.label4.Name = "label4";
            this.label4.Size = new System.Drawing.Size(47, 13);
            this.label4.TabIndex = 2;
            this.label4.Text = "Commits";
            // 
            // comboBoxStatus
            // 
            this.comboBoxStatus.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.comboBoxStatus.DisplayMember = "Description";
            this.comboBoxStatus.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.comboBoxStatus.FormattingEnabled = true;
            this.comboBoxStatus.Location = new System.Drawing.Point(15, 554);
            this.comboBoxStatus.Name = "comboBoxStatus";
            this.comboBoxStatus.Size = new System.Drawing.Size(467, 21);
            this.comboBoxStatus.TabIndex = 6;
            this.comboBoxStatus.SelectedIndexChanged += new System.EventHandler(this.onControl_Changed);
            // 
            // buttonSet
            // 
            this.buttonSet.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.buttonSet.Location = new System.Drawing.Point(488, 554);
            this.buttonSet.Name = "buttonSet";
            this.buttonSet.Size = new System.Drawing.Size(75, 23);
            this.buttonSet.TabIndex = 7;
            this.buttonSet.Text = "Set";
            this.buttonSet.UseVisualStyleBackColor = true;
            this.buttonSet.Click += new System.EventHandler(this.buttonSet_Click);
            // 
            // label5
            // 
            this.label5.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.label5.AutoSize = true;
            this.label5.Location = new System.Drawing.Point(15, 535);
            this.label5.Name = "label5";
            this.label5.Size = new System.Drawing.Size(67, 13);
            this.label5.TabIndex = 5;
            this.label5.Text = "DCO Status:";
            // 
            // textBoxMessage
            // 
            this.textBoxMessage.Font = new System.Drawing.Font("Consolas", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.textBoxMessage.Location = new System.Drawing.Point(15, 427);
            this.textBoxMessage.Multiline = true;
            this.textBoxMessage.Name = "textBoxMessage";
            this.textBoxMessage.ReadOnly = true;
            this.textBoxMessage.ScrollBars = System.Windows.Forms.ScrollBars.Both;
            this.textBoxMessage.Size = new System.Drawing.Size(548, 96);
            this.textBoxMessage.TabIndex = 4;
            this.textBoxMessage.WordWrap = false;
            // 
            // Main
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(96F, 96F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Dpi;
            this.ClientSize = new System.Drawing.Size(575, 593);
            this.Controls.Add(this.textBoxMessage);
            this.Controls.Add(this.label5);
            this.Controls.Add(this.buttonSet);
            this.Controls.Add(this.comboBoxStatus);
            this.Controls.Add(this.label4);
            this.Controls.Add(this.listViewCommits);
            this.Controls.Add(this.listViewPullRequests);
            this.Controls.Add(this.buttonPullRequests);
            this.Controls.Add(this.label3);
            this.Controls.Add(this.label2);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.textBoxRepository);
            this.Controls.Add(this.textBoxOwner);
            this.Controls.Add(this.textBoxUserName);
            this.Font = new System.Drawing.Font("Tahoma", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.Name = "Main";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "GitHub DCO Check";
            this.Load += new System.EventHandler(this.Main_Load);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.TextBox textBoxUserName;
        private System.Windows.Forms.TextBox textBoxRepository;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.Button buttonPullRequests;
        private System.Windows.Forms.ListView listViewPullRequests;
        private System.Windows.Forms.ColumnHeader columnHeaderID;
        private System.Windows.Forms.ColumnHeader columnHeaderDesc;
        private System.Windows.Forms.ColumnHeader columnHeaderState;
        private System.Windows.Forms.TextBox textBoxOwner;
        private System.Windows.Forms.Label label3;
        private System.Windows.Forms.ListView listViewCommits;
        private System.Windows.Forms.ColumnHeader columnHeaderSha;
        private System.Windows.Forms.ColumnHeader columnHeaderAuthorName;
        private System.Windows.Forms.ColumnHeader columnHeaderEmail;
        private System.Windows.Forms.Label label4;
        private System.Windows.Forms.ComboBox comboBoxStatus;
        private System.Windows.Forms.Button buttonSet;
        private System.Windows.Forms.Label label5;
        private System.Windows.Forms.TextBox textBoxMessage;
    }
}