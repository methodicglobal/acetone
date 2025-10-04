This project contains Service Fabric integration tests that depend on a locally available Service Fabric development cluster and the Service Fabric build tooling.

These tests are NOT executed in CI. They can be run locally by opening the full solution (Methodic.Acetone.sln) and running the tests in the Methodic.Acetone.IntegrationTests project.

If you do not have Service Fabric installed, these tests will fall back to mock mode or be marked Inconclusive.
