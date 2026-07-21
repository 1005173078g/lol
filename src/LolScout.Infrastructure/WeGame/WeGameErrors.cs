namespace LolScout.Infrastructure.WeGame;

public sealed class WeGameNotSignedInException : Exception
{
    public WeGameNotSignedInException() : base("The League client is not signed in.") { }
}
