namespace EduTech.Shared.Phone;


public static class PhoneNumber
{
    private const string CountryCode = "234";

  
    public static string? Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        
        string digits = new string(raw.Where(char.IsDigit).ToArray());

        
        string national;
        if (digits.Length == 11 && digits[0] == '0')
        {
            national = digits.Substring(1);                
        }
        else if (digits.Length == 13 && digits.StartsWith(CountryCode, StringComparison.Ordinal))
        {
            national = digits.Substring(3);                 
        }
        else if (digits.Length == 10)
        {
            national = digits;                             
        }
        else
        {
            return null;                                    
        }

        
        if (national[0] is not ('7' or '8' or '9'))
        {
            return null;
        }

        return "+" + CountryCode + national;
    }
}
