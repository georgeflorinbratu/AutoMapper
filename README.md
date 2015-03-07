# AutoMapper
AutoMapper

I made this automapper while using EF and ASP.NET MVC. I needed to map from EF autogenerated models to view models which had most of the properties the same name and type, but it can also be used with any other frameworks as well.

For example, you have a "Client"(Id, FirstName, LastName) EF model and a "ClientViewModel"(Id, FirstName, LastName, FullName) and you need an IQueryable<ClientViewModel>. Then you can use it like this:

public class TestMapper
{
public static readonly Expression<Func<Client, ClientViewModel>> Map_Client_ClientViewModel =
  ReflectionHelper.ExpressionMapper<Client, ClientViewModel>
  (
    x => new ClientViewModel()
    {
      FullName = x.FirstName + " " x.LastName;
    }
  );

public void TestFunction()
{
  using (var context = new DbContext())
  {
    var myQuery = context.Clients.Select(Map_Client_ClientViewModel).Where(x => x.FullName == "Some Thing");
  }
}

