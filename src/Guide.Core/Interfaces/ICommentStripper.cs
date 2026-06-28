namespace Guide.Core.Interfaces;

public interface ICommentStripper
{
    bool CanStrip(string fileExtension);
    string StripComments(string code);
}
