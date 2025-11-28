namespace Nine.Design.Login.Models
{
    /// <summary>
    /// API返回的统一格式模型（与你项目中的MessageModel保持一致）
    /// </summary>
    public class MessageModel<T>
    {
        public bool Success { get; set; }
        public string Msg { get; set; }
        public T Response { get; set; }
    }
}