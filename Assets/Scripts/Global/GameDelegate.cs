namespace DSWork.Global {
	public class GameDelegate { }

	/// <summary>动画状态机的状态事件委托</summary>
	public delegate void FSMDelegate();

	/// <summary>传递Root Motion的委托</summary>
	public delegate void AnimatorMoveDelegate(object obj);
}