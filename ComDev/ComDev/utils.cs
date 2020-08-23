using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ComDev
{
    class utils
    {
        public static int StringToHexOrDec(string strData)
        {
            int dData = -1;
            try
            {
                if ((strData.Length > 2))
                {
                    if ((strData.Substring(0, 2).Equals("0x")) || (strData.Substring(0, 2).Equals("0X")))
                    {
                        string str_sub = strData.Substring(2, strData.Length - 2);
                        dData = int.Parse(str_sub, System.Globalization.NumberStyles.HexNumber);
                    }
                    else
                    {
                        dData = int.Parse(strData, System.Globalization.NumberStyles.Integer);
                    }
                }
                else
                {
                    dData = int.Parse(strData, System.Globalization.NumberStyles.Integer);
                }
            }
            catch (Exception)
            {
                //MessageBox.Show("输入错误: " + strData, "错误");
            }
            return dData;
        }
    }
}
