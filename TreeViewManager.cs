using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace DOIMAGE
{
    public class TreeViewManager
    {
        private TreeView _treeView;
        private TextBox _txtDirectoryPath;
        private Action<string> _logMessage;
        private Action<string> _logErrorAction;

        public TreeViewManager(TreeView treeView, TextBox txtDirectoryPath, Action<string> logMessage, Action<string> logErrorAction)
        {
            _treeView = treeView ?? throw new ArgumentNullException(nameof(treeView));
            _txtDirectoryPath = txtDirectoryPath ?? throw new ArgumentNullException(nameof(txtDirectoryPath));
            _logMessage = logMessage; // Log actions can be null if not needed for all operations
            _logErrorAction = logErrorAction;
        }

        public void LoadFileSystem()
        {
            try
            {
                string? selectedNodePath = _treeView.SelectedNode?.FullPath;

                _treeView.BeginUpdate();
                _treeView.Nodes.Clear();

                if (Directory.Exists(_txtDirectoryPath.Text))
                {
                    var rootDirectoryInfo = new DirectoryInfo(_txtDirectoryPath.Text);
                    TreeNode rootNode = CreateDirectoryNode(rootDirectoryInfo);
                    _treeView.Nodes.Add(rootNode);
                    rootNode.Expand();
                }
                else
                {
                    _logErrorAction?.Invoke($"目录不存在: {_txtDirectoryPath.Text}");
                }

                if (!string.IsNullOrEmpty(selectedNodePath))
                {
                    TreeNode? nodeToSelect = FindNodeByPathRecursive(_treeView.Nodes, selectedNodePath);
                    if (nodeToSelect != null)
                    {
                        _treeView.SelectedNode = nodeToSelect;
                        nodeToSelect.Expand();
                        nodeToSelect.EnsureVisible();
                        // Note: Triggering AfterSelect event might need to be handled in Form1
                        // or by passing the Form1 instance or a specific callback.
                        // For now, we assume Form1 will handle the AfterSelect logic if still needed directly.
                    }
                }
                // UpdateJpgTotalSize(); // This was in Form1.LoadFileSystem, might need to be called from Form1 or passed as callback
            }
            catch (UnauthorizedAccessException ex)
            {
                _logErrorAction?.Invoke($"无权限访问目录: {ex.Message}");
            }
            catch (Exception ex)
            {
                _logErrorAction?.Invoke($"加载文件系统失败: {ex.Message}");
            }
            finally
            {
                _treeView.EndUpdate();
            }
        }

        private TreeNode CreateDirectoryNode(DirectoryInfo directoryInfo)
        {
            var directoryNode = new TreeNode(directoryInfo.Name);

            try
            {
                foreach (var directory in directoryInfo.GetDirectories())
                {
                    directoryNode.Nodes.Add(CreateDirectoryNode(directory));
                }

                foreach (var file in directoryInfo.GetFiles())
                {
                    string fileExtension = Path.GetExtension(file.Name).ToLower();
                    string[] commonDomainExtensions = { ".com", ".net", ".org", ".edu", ".gov", ".mil", ".int", ".info", ".biz", ".co", ".us", ".uk", ".cn" };
                    if (commonDomainExtensions.Any(ext => fileExtension.StartsWith(ext)))
                    {
                        fileExtension = ""; // Treat as extensionless if it starts with a common domain
                    }
                    string[] videoExtensions = { ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".flv", ".webm", "" };
                    if (Array.Exists(videoExtensions, ext => ext == fileExtension))
                    {
                        directoryNode.Nodes.Add(new TreeNode(file.Name));
                    }
                }
            }
            catch (UnauthorizedAccessException)
            {
                // Log or handle lack of permission for specific subdirectories/files
                _logErrorAction?.Invoke($"无权限访问: {directoryInfo.FullName} 中的部分内容");
            }
            catch (Exception ex)
            {
                 _logErrorAction?.Invoke($"创建目录节点时出错: {directoryInfo.FullName}, {ex.Message}");
            }
            return directoryNode;
        }

        public TreeNode? FindNodeByPathRecursive(TreeNodeCollection nodes, string path)
        {
            foreach (TreeNode node in nodes)
            {
                if (node.FullPath == path)
                {
                    return node;
                }
                TreeNode? foundNode = FindNodeByPathRecursive(node.Nodes, path);
                if (foundNode != null)
                {
                    return foundNode;
                }
            }
            return null;
        }

        public TreeNode? FindTreeNodeByPath(string fullPath)
        {
            try
            {
                if (string.IsNullOrEmpty(_txtDirectoryPath.Text) || _treeView.Nodes.Count == 0)
                {
                     _logErrorAction?.Invoke($"目录路径为空或TreeView没有节点，无法查找路径: {fullPath}");
                    return null;
                }

                // Ensure base path ends with a directory separator
                string basePath = _txtDirectoryPath.Text;
                if (!basePath.EndsWith(Path.DirectorySeparatorChar.ToString()))
                {
                    basePath += Path.DirectorySeparatorChar;
                }

                // Make fullPath relative to basePath
                string relativePath;
                if (fullPath.StartsWith(basePath, StringComparison.OrdinalIgnoreCase))
                {
                    relativePath = fullPath.Substring(basePath.Length);
                }
                else
                {
                    // If fullPath does not start with basePath, it might be already a relative path within the tree
                    // or an incorrect path. We try to find it directly assuming parts are from root.
                    // This case might need more robust handling depending on how fullPath is constructed.
                     _logErrorAction?.Invoke($"完整路径 '{fullPath}' 不是以基础路径 '{basePath}' 开头。尝试按部分查找。");
                     relativePath = fullPath; // Fallback, treat as relative from root if not absolute under basePath
                     // If TreeView's root node name matches the first part of txtDirectoryPath, adjust relativePath
                     if (_treeView.Nodes[0].Text.Equals(new DirectoryInfo(basePath).Name, StringComparison.OrdinalIgnoreCase))
                     {
                        // Check if fullPath is truly absolute and needs to be made relative to TreeView's display root
                        // For example, fullPath = "C:\Videos\RootFolder\SubFolder\file.mp4"
                        // _txtDirectoryPath.Text = "C:\Videos\RootFolder"
                        // _treeView.Nodes[0].Text = "RootFolder"
                        // We need path relative to "RootFolder", so "SubFolder\file.mp4"

                        string treeViewRootNodeName = _treeView.Nodes[0].Text;
                        string expectedPathPrefix = Path.Combine(_txtDirectoryPath.Text, treeViewRootNodeName);

                        //This logic is tricky because treeView.SelectedNode.FullPath returns "RootFolder\SubFolder\file.mp4"
                        //while the fullPath argument comes from Path.Combine(txtDirectoryPath.Text, node.FullPath.Remove(0, node.FullPath.IndexOf('\\') + 1));
                        //The original logic in Form1.cs for relative path was:
                        // string relativePath = fullPath.Substring(txtDirectoryPath.Text.Length).TrimStart('\\');
                        // string[] parts = relativePath.Split('\\');
                        // Let's try to stick to that if possible.

                        if (fullPath.StartsWith(_txtDirectoryPath.Text, StringComparison.OrdinalIgnoreCase))
                        {
                            relativePath = fullPath.Substring(_txtDirectoryPath.Text.Length).TrimStart(Path.DirectorySeparatorChar);
                        } else {
                            // This case means fullPath is not based on txtDirectoryPath.Text, which is unexpected for this method.
                            _logErrorAction?.Invoke($"路径 {fullPath} 与根目录 {_txtDirectoryPath.Text} 不匹配。");
                            return null;
                        }
                     }
                }


                string[] parts = relativePath.Split(Path.DirectorySeparatorChar);
                TreeNode? currentNode = _treeView.Nodes[0]; // Assuming single root node matching txtDirectoryPath

                // Validate root node matches the first part of the directory path if txtDirectoryPath is multi-level
                var rootDirInfo = new DirectoryInfo(_txtDirectoryPath.Text);
                if (!string.Equals(currentNode.Text, rootDirInfo.Name, StringComparison.OrdinalIgnoreCase))
                {
                    _logErrorAction?.Invoke($"TreeView根节点 '{currentNode.Text}' 与目录 '{rootDirInfo.Name}' 不匹配。");
                    return null;
                }


                // Traverse parts, skipping the first part if it's the root node's name
                // The original `relativePath` from Form1's `FindTreeNodeByPath` started *after* the root directory name.
                // Example: if txtDirectoryPath is "C:\Videos" and fullPath is "C:\Videos\Movies\action.mp4",
                // original relativePath was "Movies\action.mp4". `parts` would be ["Movies", "action.mp4"].
                // The treeView root node is "Videos". We need to find "Movies" under "Videos".

                foreach (string part in parts)
                {
                    if (string.IsNullOrEmpty(part)) continue; // Skip empty parts if path has double separators

                    bool found = false;
                    foreach (TreeNode child in currentNode.Nodes)
                    {
                        if (string.Equals(child.Text, part, StringComparison.OrdinalIgnoreCase))
                        {
                            currentNode = child;
                            found = true;
                            break;
                        }
                    }
                    if (!found)
                    {
                        _logErrorAction?.Invoke($"在 {currentNode.FullPath} 中无法找到节点: {part} (来自路径 {fullPath})");
                        return null;
                    }
                }
                return currentNode;
            }
            catch (Exception ex)
            {
                _logErrorAction?.Invoke($"查找节点时出错: {fullPath}, 错误: {ex.Message}");
                return null;
            }
        }


        public void ClearAllNodeColors()
        {
            if (_treeView?.Nodes == null) return;
            ClearAllNodeColorsRecursive(_treeView.Nodes);
        }

        private void ClearAllNodeColorsRecursive(TreeNodeCollection nodes)
        {
            foreach (TreeNode node in nodes)
            {
                node.BackColor = Color.Empty; // Or _treeView.BackColor if preferred
                node.ForeColor = Color.Black; // Or _treeView.ForeColor
                ClearAllNodeColorsRecursive(node.Nodes);
            }
        }

        public void ReorderDuplicateNodes(TreeNode parentNode, List<List<string>> duplicateGroups)
        {
            if (parentNode == null || parentNode.Nodes.Count == 0) return;

            var allDuplicatePaths = duplicateGroups.SelectMany(g => g).ToList();
            var normalNodes = new List<TreeNode>();
            var groupedDuplicateNodes = new List<List<TreeNode>>();

            foreach (var group in duplicateGroups)
            {
                groupedDuplicateNodes.Add(new List<TreeNode>());
            }
            
            // Use a temporary list to iterate as we'll be modifying parentNode.Nodes
            var currentNodes = parentNode.Nodes.Cast<TreeNode>().ToList();

            foreach (TreeNode node in currentNodes)
            {
                // Construct the full path for the node.
                // This requires knowing how node.FullPath is structured relative to _txtDirectoryPath.Text
                // The original Form1 logic: Path.Combine(txtDirectoryPath.Text, node.FullPath.Remove(0, node.FullPath.IndexOf('\\') + 1));
                // This implies node.FullPath is like "RootFolder\SubFolder\File.txt"
                // And txtDirectoryPath.Text is "C:\Path\To" (without RootFolder)
                // So, full path is "C:\Path\To\SubFolder\File.txt" if "RootFolder" is the first part of node.FullPath
                
                string nodeRelativePath = node.FullPath;
                // If the treeview's first node is named after the directory in _txtDirectoryPath.Text,
                // then node.FullPath starts with that directory name.
                // e.g., _txtDirectoryPath.Text = "C:\MyVideos", tree's root is "MyVideos"
                // node.FullPath might be "MyVideos\Movie1\scene.mp4"
                // We need to get "Movie1\scene.mp4" to combine with _txtDirectoryPath.Text
                string firstPathSeparator = Path.DirectorySeparatorChar.ToString();
                int firstSeparatorIndex = nodeRelativePath.IndexOf(firstPathSeparator);
                
                string actualRelativePathForCombine;
                if (firstSeparatorIndex > -1 && _treeView.Nodes.Count > 0 && nodeRelativePath.StartsWith(_treeView.Nodes[0].Text + firstPathSeparator, StringComparison.OrdinalIgnoreCase))
                {
                     actualRelativePathForCombine = nodeRelativePath.Substring(firstSeparatorIndex + 1);
                }
                else if (_treeView.Nodes.Count > 0 && nodeRelativePath.Equals(_treeView.Nodes[0].Text, StringComparison.OrdinalIgnoreCase) && firstSeparatorIndex == -1)
                {
                    // Node is the root node itself, and it's a file/item directly under the root path (no subpath in tree)
                    // This case might not happen if root is always a directory. If it's a file node at root level, path is just its name.
                    actualRelativePathForCombine = ""; // No sub-path, effectively node.Text is the item in _txtDirectoryPath.Text
                }
                else
                {
                    // This case means node.FullPath does not start with the root node's text or is the root node itself.
                    // This could happen if the tree is not built with a single root dir name node.
                    // For safety, assume node.FullPath is what needs to be appended if it doesn't contain the root dir name.
                    // Or, if the root node is the item itself.
                    // Fallback: if node.Parent is null, it's a root node. If it's the *only* root node,
                    // and it's not a directory representation from _txtDirectoryPath.Text but an item itself.
                    // This logic is becoming complex due to assumptions about tree structure.

                    // The original logic was: node.FullPath.Remove(0, node.FullPath.IndexOf('\\') + 1)
                    // This assumes node.FullPath always contains a '\'.
                    // If node.FullPath is "RootNodeName\ActualItem", it becomes "ActualItem".
                    // If node.FullPath is "ActualItem" (at root), IndexOf('\\') is -1, leading to error.
                    if (firstSeparatorIndex > -1)
                    {
                        actualRelativePathForCombine = nodeRelativePath.Substring(firstSeparatorIndex + 1);
                    }
                    else // Node is a direct child of the root directory represented by _txtDirectoryPath
                    {
                         actualRelativePathForCombine = node.Text; // Should be just the file/folder name
                         // However, Path.Combine(_txtDirectoryPath.Text, node.Text) is what we need
                         // So the string "fullPath" used for comparison should be this.
                         // Let's adjust how fullPath is created for comparison:
                    }
                }
                // Rebuild fullPath for comparison based on _txtDirectoryPath and the derived relative path
                string currentProcessingNodeFullPath;
                if (string.IsNullOrEmpty(actualRelativePathForCombine)) // True if node is the root folder's representation itself or a direct item
                {
                    if (node.Parent == null && node.Text == new DirectoryInfo(_txtDirectoryPath.Text).Name) // Node *is* the root folder
                    {
                        //This situation shouldn't occur as we iterate parentNode.Nodes, not the root node itself in this parameter.
                        //If parentNode is the root node of the tree.
                        currentProcessingNodeFullPath = Path.Combine(_txtDirectoryPath.Text, node.Text);
                    }
                    else // Node is a direct child file/folder under _txtDirectoryPath
                    {
                         currentProcessingNodeFullPath = Path.Combine(_txtDirectoryPath.Text, node.Text);
                    }
                }
                else
                {
                     currentProcessingNodeFullPath = Path.Combine(_txtDirectoryPath.Text, actualRelativePathForCombine);
                }


                if (allDuplicatePaths.Contains(currentProcessingNodeFullPath, StringComparer.OrdinalIgnoreCase))
                {
                    for (int i = 0; i < duplicateGroups.Count; i++)
                    {
                        if (duplicateGroups[i].Contains(currentProcessingNodeFullPath, StringComparer.OrdinalIgnoreCase))
                        {
                            groupedDuplicateNodes[i].Add(node);
                            break;
                        }
                    }
                }
                else
                {
                    normalNodes.Add(node);
                    if (node.Nodes.Count > 0)
                    {
                        ReorderDuplicateNodes(node, duplicateGroups); // Recurse for subdirectories
                    }
                }
            }

            parentNode.Nodes.Clear();
            foreach (var node in normalNodes) parentNode.Nodes.Add(node);
            foreach (var group in groupedDuplicateNodes)
            {
                foreach (var node in group) parentNode.Nodes.Add(node);
            }
        }
    }
}
