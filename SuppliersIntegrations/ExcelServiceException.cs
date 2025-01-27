using System;

namespace BOMVIEW.Exceptions
{
    public class ExcelServiceException : Exception
    {
        public ExcelServiceException(string message) : base(message) { }
    }
}