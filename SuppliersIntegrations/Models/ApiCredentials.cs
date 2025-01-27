namespace BOMVIEW.Models
{
    public class ApiCredentials
    {
        // DigiKey Credentials
        //public string DigiKeyClientId { get; set; } = "jHYYAjPVN8VKBBjeYmNz76Pa5sszTZKN";
        //public string DigiKeyClientSecret { get; set; } = "Ag8giFHre4JZQxgr";

        //  public string DigiKeyClientId { get; set; } = "XgjcxMcpNnV2GaVQoqQGV01GPFVKfq4w";
        // public string DigiKeyClientSecret { get; set; } = "4EABzNzt93Rxm1i7";

        // zswY3EqPWDLw1zcP3R8R0xDi5ihVR5Nf
        // XKefdc7QWmcpy9uA

        public string DigiKeyClientId { get; set; } = "jHYYAjPVN8VKBBjeYmNz76Pa5sszTZKN";
        public string DigiKeyClientSecret { get; set; } = "Ag8giFHre4JZQxgr";
        public string RedirectUri { get; set; } =  "http://localhost:5000/callback";

        public string FarnellApiKey { get; set; } = "3s4tzx72vxz7uhckrnedjeq8";


        // Mouser Credentials
        public string MouserApiKey { get; set; } = "e7e505de-6066-41a8-ac0d-1282c132c7cb";
    }
}