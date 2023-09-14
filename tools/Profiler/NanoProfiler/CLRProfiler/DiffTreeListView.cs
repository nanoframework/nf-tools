using System;
using System.IO;
using System.Drawing;
using System.Collections;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using System.Text;




namespace CLRProfiler
{
	internal interface IDiffTreeOwner
	{
		ArrayList ProcessNodes(object obj, ArrayList nodesAtOneLevel);

		Font GetFont(object obj, TreeNodeBase node);

		Color GetColor(object obj, TreeNodeBase node, bool positive);

		object GetInfo(object obj, TreeNodeBase node, ColumnInformation info);

		ArrayList FetchKids(object obj, TreeNodeBase node);
		
	}

	internal class DiffResizeBarCapture : Control
	{
		int track;
		ArrayList columns;

		internal DiffResizeBarCapture(ArrayList in_columns) : base()
		{
			track = -1;
			columns = in_columns;
		}

		internal void SetCapture(int in_track)
		{
			track = in_track;
			this.Capture = true;
		}

		override protected void OnMouseMove(MouseEventArgs e)
		{
			if(track != -1)
			{
				DiffColumn left = (DiffColumn)columns[track];

				int ll = left.Left;
				if(e.X < ll + 15)
				{
					return;
				}
				left.Width = e.X - left.Left;

				((DiffTreeListView)Parent).RepaintTreeView();
			}
		}

		override protected void OnMouseUp(MouseEventArgs e)
		{
			track = -1;
			this.Capture = false;
		}
	}
	internal class DiffColumn : Button
	{
		private bool pressed;
		private ArrayList columnsRef;
		private ColumnInformation ci;
		private DiffResizeBarCapture resizeBar;

		internal ColumnInformation ColumnInformation {get {return ci;}}

		internal DiffColumn(ColumnInformation in_ci, DiffResizeBarCapture in_resizeBar,  ArrayList in_columnsRef) : base()
		{
			pressed = false;

			ci = in_ci;
			resizeBar = in_resizeBar;
			columnsRef = in_columnsRef;

			Text = ci.Text;

			SetStyle(ControlStyles.Selectable, false);
		}

		override protected void OnMouseMove(MouseEventArgs e)
		{
			Cursor oldCursor = Cursor, toSet = Cursors.Arrow;
			if(pressed)
			{
				base.OnMouseMove(e);
			}
			else
			{
				int index = columnsRef.IndexOf(this);
				int left = 0;
				int right = this.Width;
				if(e.X - left < 5 && index != 0 || right - e.X < 5)
				{
					toSet = Cursors.VSplit;
				}
			}

			if(oldCursor != toSet)
			{
				Cursor = toSet;
			}
		}

		override protected void OnMouseDown(MouseEventArgs e)
		{
			if(e.Button == MouseButtons.Left)
			{
				int index = columnsRef.IndexOf(this);
				int left = 0;
				int right = this.Width;
				if(e.X - left < 5 && index != 0)
				{
					resizeBar.SetCapture(index - 1);
					return;
				}
				else if(right - e.X < 5)
				{
					resizeBar.SetCapture(index);
					return;
				}
			}

			pressed = true;
			base.OnMouseDown(e);
		}

		override protected void OnMouseUp(MouseEventArgs e)
		{
			pressed = false;
			base.OnMouseUp(e);
		}
	}

	internal class DiffTreeListView : Control
	{
		/// <summary>
		/// Required designer variable.
		/// </summary>
		private System.ComponentModel.Container components = null;

		#region Data member
		private int leftEdge;
		private ArrayList columns;
		private ListBox treeListBox;
		private DiffResizeBarCapture resizeBar;
		private object keepSelected;
		private PlacedToolTip hoverPopup;

		// events of interest
		internal event EventHandler ColumnClick;
		internal event EventHandler SelectedIndexChanged;

		//
		internal object TokenObject = null;
		private IDiffTreeOwner treeOwner = null;

		private int ContextSelection;

		private TreeNodeBase root = null;
		internal TreeNodeBase Root
		{
			set
			{
				if(value != null)
				{
					treeListBox.Items.Clear();
					treeListBox.Items.Add(root = value);
					root.depth = 0;
				}
			}
			get
			{
				return root;
			}
		}

		enum Direction
		{
			Forward,
			Backward
		}
		#endregion

		#region ListBox
		internal ListBox.ObjectCollection Items
		{
			get
			{
				return treeListBox.Items;
			}
		}

		internal object SelectedItem
		{
			get
			{
				return treeListBox.SelectedItem;
			}
			set
			{
				treeListBox.SelectedItem = value;
			}
		}

		private void Resort(int depth, TreeNodeBase root)
		{
			root.depth = depth;
			treeListBox.Items.Add(root);
			if(root.allkids != null && root.IsExpanded)
			{
				ArrayList nodes = treeOwner.ProcessNodes(TokenObject, root.allkids);
				for(int i = 0; i < nodes.Count; i++)
				{
					Resort(1 + depth, (TreeNodeBase)nodes[i]);
				}
			}
		}

		internal void Resort()
		{
			keepSelected = treeListBox.SelectedItem;
			treeListBox.Items.Clear();
			Resort(0, root);
		}

		private void ReplaceContents(object[] nodes)
		{
			/* an attempt to prevent the control from jerking */
			object selection = treeListBox.Items[treeListBox.SelectedIndex];
			int topIndex = treeListBox.TopIndex;
			int leftEdge = GetScrollPos(treeListBox.Handle, 0);

			treeListBox.BeginUpdate();
			treeListBox.Items.Clear();
			treeListBox.Items.AddRange(nodes);
			treeListBox.SelectedItem = selection;
			treeListBox.TopIndex = topIndex;
			treeListBox.EndUpdate();

			/* send WM_HSCROLL message to the list box */
			SendMessage(treeListBox.Handle, 0x0114, 4 + (leftEdge << 16), 0);
		}

		private int AddAfter(int index, TreeNodeBase root)
		{
			if(root.allkids == null)
			{
				root.allkids = treeOwner.FetchKids(TokenObject, root);
			}
			ArrayList nodes = treeOwner.ProcessNodes(TokenObject, root.allkids);

			int numNodes = nodes.Count, nodesInList = treeListBox.Items.Count;
			int costOfRebuilding = 3 * (numNodes + nodesInList);
			int costOfInsertions = (1 + nodesInList - index) * numNodes;
			int i, newDepth = 1 + root.depth;

			if(costOfInsertions < costOfRebuilding)
			{
				foreach(TreeNodeBase curr in nodes)
				{
					curr.depth = newDepth;
					treeListBox.Items.Insert(++index, curr);
				}
			}
			else
			{
				++index;
				ListBox.ObjectCollection items = treeListBox.Items;
				object[] allNodes = new object[numNodes + nodesInList];
				for(i = 0; i < index; i++)
				{
					allNodes[i] = items[i];
				}
				for(; i < index + numNodes; i++)
				{
					allNodes[i] = nodes[i - index];
					((TreeNodeBase)allNodes[i]).depth = newDepth;
				}
				for(; i < numNodes + nodesInList; i++)
				{
					allNodes[i] = items[i - numNodes];
				}
				ReplaceContents(allNodes);
			}
			return index;
		}

		override protected void OnFontChanged(EventArgs e)
		{
			base.OnFontChanged(e);
			treeListBox.Font = Font;
			treeListBox.ItemHeight = Font.Height + 3;
		}

		#endregion

		internal DiffTreeListView(IDiffTreeOwner in_treeOwner)
		{
			// This call is required by the Windows.Forms Form Designer.
			InitializeComponent();

			treeOwner = in_treeOwner;

			hoverPopup = new PlacedToolTip();
			hoverPopup.Parent = FindForm();

			treeListBox = new ListBox();
			treeListBox.Parent = this;
			treeListBox.BorderStyle = System.Windows.Forms.BorderStyle.None;
			treeListBox.DrawMode = System.Windows.Forms.DrawMode.OwnerDrawFixed;
			treeListBox.IntegralHeight = false;
			treeListBox.Name = "treeListBox";
			treeListBox.KeyDown += new System.Windows.Forms.KeyEventHandler(this.treeListBox_KeyDown);
			treeListBox.MouseDown += new System.Windows.Forms.MouseEventHandler(this.treeListBox_MouseDown);
			treeListBox.MouseMove += new System.Windows.Forms.MouseEventHandler(this.treeListBox_MouseMove);
			treeListBox.DoubleClick += new System.EventHandler(this.treeListBox_DoubleClick);
			treeListBox.DrawItem += new System.Windows.Forms.DrawItemEventHandler(this.treeListBox_DrawItem);
			treeListBox.SelectedIndexChanged += new System.EventHandler(this.treeListBox_SelectedIndexChanged);

			columns = new ArrayList();

			// must be created before calling `AddColumn`
			resizeBar = new DiffResizeBarCapture(columns);
			resizeBar.Visible = false;
			resizeBar.Parent = this;

			leftEdge = 0;

			OnResize(null);

			// Create a blank context menu.  We'll fill it in when the user right clicks
			// TODO ContextMenu is no longer supported. Use ContextMenuStrip instead. For more details see https://docs.microsoft.com/en-us/dotnet/core/compatibility/winforms#removed-controls
						ContextMenu contextMenu = new ContextMenu();
			// TODO ContextMenu is no longer supported. Use ContextMenuStrip instead. For more details see https://docs.microsoft.com/en-us/dotnet/core/compatibility/winforms#removed-controls
			ContextMenu = contextMenu;
		}

		internal void RepaintTreeView()
		{
			RedoColumnLayout();
			treeListBox.Invalidate();
			treeListBox.Update();
		}
		/// <summary>
		/// Clean up any resources being used.
		/// </summary>
		protected override void Dispose( bool disposing )
		{
			if( disposing )
			{
				if( components != null )
					components.Dispose();
			}
			base.Dispose( disposing );
		}

		#region Component Designer generated code
		/// <summary>
		/// Required method for Designer support - do not modify 
		/// the contents of this method with the code editor.
		/// </summary>
		private void InitializeComponent()
		{
			components = new System.ComponentModel.Container();
		}
		#endregion

		protected override void OnPaint(PaintEventArgs pe)
		{
			// need to add custom paint code here

			// Calling the base class OnPaint
			base.OnPaint(pe);
		}

		void RedoColumnLayout()
		{
			int position = -leftEdge;
			int height = Font.Height + 5;
			for(IEnumerator e = columns.GetEnumerator(); e.MoveNext();)
			{
				DiffColumn current = (DiffColumn)e.Current;
				current.SuspendLayout();
				current.Location = new Point(position, 0);
				if(height > 0)
				{
					current.Height = height;
				}
				current.ResumeLayout();
				position += current.Width;
			}

			position += leftEdge;
			treeListBox.HorizontalScrollbar = (position > treeListBox.ClientSize.Width);
			treeListBox.HorizontalExtent = position;
		}

		[DllImport("User32.Dll")]
		internal static extern int GetScrollPos(IntPtr hwnd, int bar);

		[DllImport("User32.Dll")]
		internal static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);

		#region Event -- ListBox
		private void treeListBox_DrawItem(object sender, System.Windows.Forms.DrawItemEventArgs e)
		{
			/* 0 stands for SB_HORZ */
			int leftEdge = GetScrollPos(treeListBox.Handle, 0);
			if(leftEdge != this.leftEdge)
			{
				this.leftEdge = leftEdge;
				RedoColumnLayout();
			}

			int position = 0;

			Graphics g = e.Graphics;
			ListBox treeView = treeListBox;

			if(e.Index < 0 || e.Index >= treeView.Items.Count)
			{
				return;
			}

			StringFormat sf = new StringFormat(StringFormatFlags.NoWrap);
			sf.Trimming = StringTrimming.EllipsisCharacter;

			TreeNodeBase node = (TreeNodeBase)Items[e.Index];

			int crossover = (treeListBox.ItemHeight - 1) * (1 + node.depth);
			g.FillRectangle(new SolidBrush(Color.White), position, e.Bounds.Top, crossover, e.Bounds.Height);

			Rectangle itemRect = new Rectangle(crossover, e.Bounds.Top, e.Bounds.Right - crossover, e.Bounds.Height);
			g.FillRectangle(new SolidBrush(e.BackColor), itemRect);

			if(e.State == DrawItemState.Focus)
			{
				ControlPaint.DrawFocusRectangle(g, itemRect, e.ForeColor, e.BackColor);
			}

			Pen grayPen = new Pen(Color.LightGray);
			g.DrawLine(grayPen, 0, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);

			Font fontToUse = treeOwner.GetFont(TokenObject, node);
			

			Color color = treeOwner.GetColor(TokenObject, node, (e.State & DrawItemState.Selected) != DrawItemState.Selected);

			Brush brush = new SolidBrush(color);

			Region oldClip = g.Clip;
			foreach(DiffColumn c in columns)
			{
				ColumnInformation current = c.ColumnInformation;
				Rectangle rect = new Rectangle(position, e.Bounds.Top, c.Width, e.Bounds.Height);
				g.Clip = new Region(rect);

				string res = treeOwner.GetInfo(TokenObject, node, current).ToString();
				
				if(current.ColumnType == ColumnInformation.ColumnTypes.Tree)
				{
					rect.Offset((1 + node.depth) * (treeListBox.ItemHeight - 1), 0);
					rect.Width -= (1 + node.depth) * (treeListBox.ItemHeight - 1);

					if(node.HasKids)
					{
						Pen p = new Pen(Color.Gray);
						int y0 = e.Bounds.Top;
						int x0 = position + node.depth * (treeListBox.ItemHeight - 1);
						g.DrawRectangle(p, x0 + 3, y0 + 3, (treeListBox.ItemHeight - 9), (treeListBox.ItemHeight - 9));
						g.DrawLine(p, x0 + 5, y0 + (treeListBox.ItemHeight - 3) / 2, x0 + (treeListBox.ItemHeight - 8), y0 + (treeListBox.ItemHeight - 3) / 2);
						if(!node.IsExpanded)
						{
							g.DrawLine(p, x0 + (treeListBox.ItemHeight - 3) / 2, y0 + 5, x0 + (treeListBox.ItemHeight - 3) / 2, y0 + (treeListBox.ItemHeight - 8));
						}
					}
				}

				if(res != null)
				{
					int characters, lines;

					SizeF layoutArea = new SizeF(rect.Width, rect.Height);
					SizeF stringSize = g.MeasureString(res, fontToUse, layoutArea, sf, out characters, out lines);

					g.DrawString(res.Substring(0, characters) + (characters < res.Length ? "..." : ""), fontToUse, brush, rect.Location, sf);
					g.DrawLine(grayPen, rect.Right - 1, e.Bounds.Top, rect.Right - 1, e.Bounds.Bottom - 1);
				}

				position += c.Width;
			}
			g.Clip = oldClip;
		}

		private void treeListBox_MouseMove(object s, MouseEventArgs e)
		{
			int index = treeListBox.IndexFromPoint(e.X, e.Y);
			if(index < 0 || index >= Items.Count)
			{
				return;
			}

			TreeNodeBase node = (TreeNodeBase)Items[index];
			int crossover = (treeListBox.ItemHeight - 1) * (1 + node.depth) - leftEdge;
			Rectangle rect = treeListBox.GetItemRectangle(index);

			Point screenPoint = treeListBox.PointToScreen(new Point(crossover, rect.Top));
			if(hoverPopup.Parent == null)
			{
				hoverPopup.Parent = FindForm();
			}
			Point controlPoint = hoverPopup.Parent.PointToClient(screenPoint);

			string textToDisplay = treeOwner.GetInfo(TokenObject, node, null).ToString();
			if(textToDisplay != hoverPopup.CurrentlyDisplayed())
			{
				hoverPopup.Display(controlPoint, treeOwner.GetFont(TokenObject, node), rect.Height, textToDisplay);
			}
		}

		private void treeListBox_MouseDown(object s, MouseEventArgs e)
		{
			int index = treeListBox.IndexFromPoint(e.X, e.Y);
			if(index == ListBox.NoMatches)
			{
				return;
			}

			TreeNodeBase node = (TreeNodeBase)Items[index];
			if (e.Button == MouseButtons.Left)
			{
				int offset = node.depth * (treeListBox.ItemHeight - 1) - leftEdge;
				if(offset <= e.X && e.X < offset + (treeListBox.ItemHeight - 1))
				{
					ToggleBranch(index);
				}
			}
			else
			{
				//  Customize the context menu
				// TODO ContextMenu is no longer supported. Use ContextMenuStrip instead. For more details see https://docs.microsoft.com/en-us/dotnet/core/compatibility/winforms#removed-controls
								ContextMenu contextMenu = this.ContextMenu;
				ColumnInformation ci = ((DiffColumn)columns[0]).ColumnInformation;
				string fnName = treeOwner.GetInfo(TokenObject, node, ci).ToString();
				
				

				ContextSelection = index;
				EventHandler eventHandler = new EventHandler( this.ContextMenu_Selection );
				contextMenu.MenuItems.Clear();
				// TODO MenuItem is no longer supported. Use ToolStripMenuItem instead. For more details see https://docs.microsoft.com/en-us/dotnet/core/compatibility/winforms#removed-controls
				contextMenu.MenuItems.Add( new MenuItem( "Save to...", eventHandler));			
			}
			
		}

		private void ContextMenu_Selection(object sender, System.EventArgs e) 
		{
			// output
			StringBuilder sb = new StringBuilder("Function Name, Prev(Incl), Curr(Incl), Diff(Incl), Prev(Calls), Curr(Calls), Diff(Calls)\r\n");
			// TODO MenuItem is no longer supported. Use ToolStripMenuItem instead. For more details see https://docs.microsoft.com/en-us/dotnet/core/compatibility/winforms#removed-controls
			MenuItem miClicked = (MenuItem)sender;

			string fileName = GetSaveAsFileName();
			
			switch (miClicked.Index)
			{

				case 0:
					// Save Selected item
					if(fileName != null)
					{					
						GetTreeContent(sb, this.Root as DiffDataNode);
					}
					break;

				case 1:
					// Save forward
					
					break;
				case 2:
					// Save backward
					
					break;
			}
			if(sb.Length > 0)
			{
				writeToFile(fileName,sb);
			}
		}

		private void GetTreeContent(StringBuilder sb, DiffDataNode root)
		{
			string prefx = "-";
			
			for(int i = 0; i < root.depth; i++)
			{
				prefx += '-';
			}
			sb.AppendFormat("{0}{1},{2},{3},{4},{5},{6},{7}\r\n", prefx, root.name, root.prevIncl, root.currIncl.ToString(), root.diffIncl.ToString(), root.prevCalls.ToString(), root.currCalls.ToString(), root.diffCalls.ToString());
			
			if(root.IsExpanded)
			{
				for(int i = 0; i < root.allkids.Count; i++)
				{
					GetTreeContent(sb, root.allkids[i] as DiffDataNode);
				}
			}

		}
		private string GetSaveAsFileName()
		{
			SaveFileDialog saveFileDialog1 = new SaveFileDialog();
 
			saveFileDialog1.Filter = "txt files (*.txt)|*.txt|All files (*.*)|*.*"  ;
			saveFileDialog1.FilterIndex = 2 ;
			saveFileDialog1.RestoreDirectory = true ;
			
			if(saveFileDialog1.ShowDialog() == DialogResult.OK)
			{
				return saveFileDialog1.FileName;
			}
			return null;

		}
		internal void writeToFile(string fileName, StringBuilder sb)
		{
			try
			{
				if(File.Exists(fileName))
					File.Delete(fileName);
				string path = Path.GetDirectoryName(fileName);
			
				// Determine whether the directory exists.
				if (!Directory.Exists(path)) 
				{
					// Create the directory it does not exist.
					Directory.CreateDirectory(path);
				}
				FileStream fs = new FileStream(fileName, FileMode.CreateNew, FileAccess.Write, FileShare.ReadWrite);
				StreamWriter sw = new StreamWriter(fs);
				sw.Write(sb);
				sw.Close();
			}
			catch(Exception e)
			{
				
				throw new Exception(e.Message);
			}
		}
		private void treeListBox_DoubleClick(object s, EventArgs e)
		{
			int index = treeListBox.SelectedIndex;
			if(index != -1)
			{
				ToggleBranch(index);
			}
		}

		override protected void OnResize(EventArgs e)
		{
			treeListBox.Size = new Size(ClientSize.Width,
				ClientSize.Height - treeListBox.Top);
			treeListBox.Refresh();
		}

		private void treeListBox_KeyDown(object s, KeyEventArgs e)
		{
			int index = treeListBox.SelectedIndex;
			if(index == -1)
			{
				return;
			}

			Keys key = e.KeyCode;
			TreeNodeBase node = (TreeNodeBase)Items[index];
			switch(key)
			{
				case Keys.Left:
					if(node.IsExpanded)
					{
						ToggleBranch(index);
					}
					break;

				case Keys.Right:
					if(!node.IsExpanded)
					{
						ToggleBranch(index);
					}
					break;

				case Keys.Space:
					ToggleBranch(index);
					break;
			}
		}

		private void treeListBox_SelectedIndexChanged(object sender, System.EventArgs e)
		{
			if(SelectedIndexChanged != null)
			{
				SelectedIndexChanged(sender, e);
			}
		}

		#endregion

		#region Event -- Column button
		private void DiffColumn_Click(object sender, EventArgs e)
		{
			if(ColumnClick != null)
			{
				ColumnClick(sender, e);
			}
		}

		internal ArrayList GetColumns()
		{
			return columns;
		}

		internal DiffColumn AddColumn(ColumnInformation ci, int defaultWidth)
		{
			DiffColumn c = new DiffColumn(ci, resizeBar, columns);

			c.Width = defaultWidth;
			c.Parent = this;
			c.TextAlign = ContentAlignment.MiddleLeft;
			c.Click += new EventHandler(DiffColumn_Click);
			c.Font = Font;
			columns.Add(c);

			RedoColumnLayout();

			treeListBox.SuspendLayout();
			treeListBox.Location = new Point(0, c.Height);
			treeListBox.Size = new Size(ClientSize.Width, ClientSize.Height - c.Height);
			treeListBox.ResumeLayout();

			return c;
		}
		#endregion

		void ToggleBranch(int index)
		{
			TreeNodeBase node = (TreeNodeBase)Items[index];
			if(!node.HasKids)
			{
				return;
			}

			int count = Items.Count;
			if(node.IsExpanded)
			{
				index++;
				int upTo = index;
				TreeNodeBase kid = null;
				// eh, hopefully item access is cheap

				ListBox.ObjectCollection items = treeListBox.Items;

				while(upTo < count && (kid = (TreeNodeBase)items[upTo]).depth > node.depth)
				{
					kid.IsExpanded = false;
					upTo++;
				}

				int i, nodesInList = items.Count;
				int toRemove = upTo - index;
				int costOfRemoveAt = toRemove * (1 + nodesInList - upTo);
				int costOfRebuilding = 2 * nodesInList + 3 * (nodesInList - (upTo - index));

				if(costOfRemoveAt < costOfRebuilding)
				{
					for(i = upTo; i-- > index;)
					{
						items.RemoveAt(i);
					}
				}
				else
				{
					object[] allNodes = new object[nodesInList - toRemove];
					for(i = 0; i < index; i++)
					{
						allNodes[i] = items[i];
					}
					for(i = upTo; i < nodesInList; i++)
					{
						allNodes[i - toRemove] = items[i];
					}

					ReplaceContents(allNodes);
				}

				node.IsExpanded = false;
			}
			else
			{
				AddAfter(index, node);
				node.IsExpanded = true;
			}
		}


		
	}
	

	
	
	
	

	
	
}
