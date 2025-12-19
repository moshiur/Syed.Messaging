namespace Syed.BuildingBlocks.IntegrationEvents
{
    public class UserRegisteredEvent
    {
        public Guid UserId { get; set; }
        public string Email { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }

        public UserRegisteredEvent() { }

        public UserRegisteredEvent(Guid userId, string email, string firstName, string lastName)
        {
            UserId = userId;
            Email = email;
            FirstName = firstName;
            LastName = lastName;
        }
    }
}
