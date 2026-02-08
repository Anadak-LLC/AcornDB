AcornDB NOTICE
==============

Copyright (c) 2025 Anadak LLC  
All rights reserved.

-------------------------------------------------------------------------------
Project Overview
-------------------------------------------------------------------------------

**AcornDB** is a lightweight, embedded, reactive, and synchronizable document
database engine for .NET, designed for developers who value local-first
performance, minimalism, and joy in building.

This project is maintained by **Anadak LLC**, and may be freely used for
noncommercial purposes under the PolyForm Noncommercial License 1.0.0.

For commercial licensing, enterprise support, or partnership inquiries, please contact:
licensing@anadakcorp.com

-------------------------------------------------------------------------------
Licensing Summary
-------------------------------------------------------------------------------

This software is made available under a **dual license**:

1. **Community (Noncommercial)**  
   Licensed under the PolyForm Noncommercial License 1.0.0  
   https://polyformproject.org/licenses/noncommercial/1.0.0/

2. **Commercial License**  
   Commercial use requires a separate license agreement with Anadak LLC.  
   Contact: licensing@anadak.com

-------------------------------------------------------------------------------
Attribution
-------------------------------------------------------------------------------

If you redistribute or reference this software, please include the following:

    "Powered by AcornDB — a project by Anadak LLC (https://www.anadakcorp.com)"

-------------------------------------------------------------------------------
Strong Name Signing
-------------------------------------------------------------------------------

AcornDB uses strong name signing for assembly identity. The key file is not
committed to source control and must be generated locally.

**To generate the key file:**

```bash
cd AcornDB
sn -k AcornDBKey.snk
```

Or on .NET Core / .NET 5+:

```bash
dotnet tool install -g dotnet-sn
dotnet-sn -k AcornDB/AcornDBKey.snk
```

**Note**: The project uses `PublicSign=true` for delay signing, which allows
building without the private key for most scenarios. Full signing is only
required for GAC deployment or when referenced by other strong-named assemblies.

-------------------------------------------------------------------------------
Trademark Notice
-------------------------------------------------------------------------------

**AcornDB**, **OakTree**, and **Anadak** are trademarks or registered trademarks
of Anadak LLC. Use of these marks without written permission is prohibited,
except as permitted for attribution under the open license terms.
