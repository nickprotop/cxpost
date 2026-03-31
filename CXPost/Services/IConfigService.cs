using CXPost.Models;

namespace CXPost.Services;

public interface IConfigService
{
    CXPostConfig Load();
    void Save(CXPostConfig config);
}
