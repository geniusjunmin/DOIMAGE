using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace DOIMAGE
{
    /// <summary>
    /// WinForm本地化语言管理
    /// </summary>
    public static class LanguageManager
    {
        /// <summary>
        /// 常用语言
        /// </summary>
        public static class LangKeys
        {
            public const string zh_CN = "zh-CN";
            public const string zh_TW = "zh-TW";
            public const string en_US = "en-US";
        }

        private static void ApplyResources(ComponentResourceManager resources, Control root, string Name)
        {

            foreach (var item in root.Controls)
            {
                Control? ctl = item as Control;
                if (ctl != null)
                {
                    ApplyResources(resources, ctl, ctl.Name);
                }
            }
            resources.ApplyResources(root, Name);
        }
        /// <summary>
        /// 改变窗体语言
        /// </summary>
        /// <param name="root">窗体</param>
        /// <param name="LanguageKey">语言Key带后缀的(如:zh-CN)</param>
        public static void ChangeLanguage(Form root, string LanguageKey)
        {
            Thread.CurrentThread.CurrentUICulture = new CultureInfo(LanguageKey);
            ComponentResourceManager resources = new ComponentResourceManager(root.GetType());

            ApplyResources(resources, root, root.Name);
            root.Text = resources.GetString("$this.Text");
        }
    }
}
