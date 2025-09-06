public interface IHittable {
    public bool TryApplyHit();
    protected void Hit();
}