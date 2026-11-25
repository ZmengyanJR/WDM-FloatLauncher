using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace WDM.ViewModels
{
    /// <summary>
    /// ViewModel 基类，实现 INotifyPropertyChanged，简化属性变更通知。
    /// 所有 ViewModel 都应继承此类，通过 SetProperty 触发 UI 更新。
    /// </summary>
    public abstract class ObservableObject : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>
        /// 设置属性值，若值发生变化则触发 PropertyChanged 事件。
        /// </summary>
        /// <typeparam name="T">属性类型</typeparam>
        /// <param name="field">属性对应的字段引用</param>
        /// <param name="value">新值</param>
        /// <param name="propertyName">属性名，由编译器自动填充</param>
        /// <returns>值是否发生了变化</returns>
        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (Equals(field, value))
                return false;

            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            return true;
        }

        /// <summary>
        /// 主动触发某个属性的变更通知（用于依赖其他属性计算的场景）。
        /// </summary>
        protected void RaisePropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
