# Schema-less Node-Resolved Assets

Static assets are modelled as schema-less values resolved through the org tree and backed by Azure Blob Storage. This deliberately mirrors config inheritance while avoiding config schemas because asset keys are operational slots like `kiosk.background-video`, not typed configuration contracts; file bytes stay outside Marten to avoid storing large binary payloads in the event/read model database.
