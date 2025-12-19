namespace Syed.BuildingBlocks
{
    public abstract class Entity
    {
        public Guid Id { get; protected set; }
        
        // Soft delete support
        public bool IsDeleted { get; protected set; }
        public DateTime? DeletedAtUtc { get; protected set; }

        private List<IDomainEvent> _domainEvents;
        public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents?.AsReadOnly();

        protected Entity()
        {
            Id = Guid.NewGuid();
            IsDeleted = false;
        }
        
        public virtual void SoftDelete()
        {
            IsDeleted = true;
            DeletedAtUtc = DateTime.UtcNow;
        }
        
        public virtual void Restore()
        {
            IsDeleted = false;
            DeletedAtUtc = null;
        }

        public void AddDomainEvent(IDomainEvent domainEvent)
        {
            _domainEvents = _domainEvents ?? new List<IDomainEvent>();
            _domainEvents.Add(domainEvent);
        }

        public void RemoveDomainEvent(IDomainEvent domainEvent)
        {
            _domainEvents?.Remove(domainEvent);
        }

        public void ClearDomainEvents()
        {
            _domainEvents?.Clear();
        }

        public override bool Equals(object? obj)
        {
            if (obj is not Entity other)
                return false;

            if (ReferenceEquals(this, other))
                return true;

            if (GetType() != other.GetType())
                return false;

            if (Id == Guid.Empty || other.Id == Guid.Empty)
                return false;

            return Id == other.Id;
        }

        public override int GetHashCode()
        {
            return (GetType().ToString() + Id).GetHashCode();
        }

        public static bool operator ==(Entity left, Entity right)
        {
            if (ReferenceEquals(left, null) && ReferenceEquals(right, null))
                return true;

            if (ReferenceEquals(left, null) || ReferenceEquals(right, null))
                return false;

            return left.Equals(right);
        }

        public static bool operator !=(Entity left, Entity right)
        {
            return !(left == right);
        }
    }
}
