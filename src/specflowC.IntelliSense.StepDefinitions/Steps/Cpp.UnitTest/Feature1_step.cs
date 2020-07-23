using System;
using TechTalk.SpecFlow;

namespace CppUnitTest
{
    [Binding]
    public class Feature1
    {
        [Given]
public void GivenASentence()
{
    ScenarioContext.Current.Pending();
}

        [When]
public void WhenASentence()
{
    ScenarioContext.Current.Pending();
}

        [Then]
public void ThenASentence()
{
    ScenarioContext.Current.Pending();
}
    }
}
