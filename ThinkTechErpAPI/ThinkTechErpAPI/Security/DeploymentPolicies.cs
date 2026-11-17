namespace ThinkTechErpAPI.Security;

public static class DeploymentPolicies
{
    /// <summary>DEPLOYMENT.V or DEPLOYMENT.M: read organizations' deployment information.</summary>
    public const string View = "DeploymentView";

    /// <summary>DEPLOYMENT.M: add, edit and delete it.</summary>
    public const string Manage = "DeploymentManage";
}
