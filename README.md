# Introduction to Acetone

#### Background

**Methodic's Acetone** allows a service fabric cluster to run on dynamic ports and/or hostnames, without exposing the services directly. This enables access to all APIs over the same SSL/TLS port 443 and IP address, even though the services themselves are on an internal network, using various port numbers and over various hosts according to your service fabric cluster deployment.

Due to a lack of off-the-shelf solutions for service fabric reverse proxies, **Methodic** custom built one specifically for Methodic needs, and named it **Acetone**

#### How it works

::: mermaid
graph LR;
 EXT-->IIS[Methodic Acetone on IIS 443]
 IIS-->SF1(SF Stateless Reliable Service 80443);
 IIS-->SF2(SF Stateless Reliable Service 90443);
 IIS-->SF3(SF Stateless Reliable Service 70443);
 IIS-->SF4(SF Stateless Reliable Service 60443);
:::

When the IIS process starts (`w3wp.exe`), the module is initialized at which point it determines the configuration and establishes a connection to the service fabric cluster.

Upon each HTTP request that matches the rewrite rule criteria within IIS, Acetone's Rewrite function is then invoked. This will determine which micro-service is to be called according to the HTTP request (eg subdomain) and configuration (discussed later), connect to the service fabric cluster endpoint configured, determine a single endpoint host/port combination, and rewrite a request to that destination instead.

# Installation
### Windows OS
Install IIS
Install IIS Rewrite Module
Install IIS Application Request Routing Module

### Acetone
Copy over all DLLs to IIS server
Install the Acetone DLL into the GAC

### IIS
Open the URL Rewrite module on server level in IIS Manager
Go to View Providers under Actions
Select Add Provider
Choose a name (eg `Acetone`) and select Methodic Acetone from Managed Type dropdown
Press OK and continue reading for configuration

# Configuration

## General

#### Cluster Connection String (`ClusterConnectionString`) (required)
Connection string for the service fabric cluster to discover. Can be a comma-separated list for multiple endpoints

eg _https://my-cluster-ss-lb.methodic.com:66042_


#### Application Name Location (`ApplicationNameLocation`) (default: `Subdomain`)
One of the following options: `Subdomain`, `SubdomainPostHyphens`, `SubdomainPreHyphens` or `FirstUrlFragment`.
Defaults to `Subdomain` if not supplied, or value cannot be parsed.

The name of the application/service (eg _mycoolservice_) is derived from:

- `Subdomain` : the very subdomain eg https://_mycoolservice_.methodic.com

- `SubdomainPostHyphens` : the subdomain after the last hyphen, eg https://uat-_mycoolservice_.methodic.com

- `SubdomainPreHyphens` : the subdomain before the first hyphen, eg https://_mycoolservice_-uat.methodic.com

- `FirstUrlFragment` : the first URL segment eg https://uat.methodic.com/_mycoolservice_

#### Partition Cache Limit (`PartitionCacheLimit`) (default: 5)
The maximum number of cached location entries on the client

#### Log Information (`LogInformation`) (default: `False`)
`True` or `False` for logging information messages into event log

## Credentials

You'll need to decide if you would like to use Thumbprints or Common Names for X509 security.
> Note: Only X509 certificate based security is currently supported - Windows/Azure Active Directory is coming soon

## Credentials > Certificates via Thumbprints

To configure Methodic Acetone to use Thumbprints for X509 certificate credentials please set the following values

#### Credentials Type (`CredentialsType`) 
Set to `CertificateThumbprint` within IIS

#### Client Certificate Thumbprint (`ClientCertificateThumbprint`)
Certificate thumbprint of the client certificate - certificate needs to be installed in `LocalMachine.My` and the IIS process must have permissions to read the private key

#### Server Certificate Thumbprint (`ServerCertificateThumbprint`)
Certificate thumbprint of the certificate installed on the cluster as `ServerCertificate` -> see cluster manifest


## Credentials > Certificate via Common Name 

To configure Methodic Acetone to use Common Names for X509 certificate credentials please set the following values

#### Credentials Type (`CredentialsType`) 
Set to `CertificateCommonName` within IIS

#### Client Certificate Subject Distinguished Name (`ClientCertificateSubjectDistinguishedName`)
Certificate CN=User name for connecting to the cluster

eg _CN=Methodic Global_

#### Client Certificate Issuer Distinguished Name (`ClientCertificateIssuerDistinguishedName`)
Certificate issuer distinguished name for connecting to the cluster. 

eg _CN=Sectigo RSA Domain Validation Secure Server CA, O=Sectigo Limited, L=Salford, S=Greater Manchester, C=GB_

#### Server/Remote Common Names (Optional, Comma Separated) (`ServerCertificateCommonNames`)
Comma separated list of Common Names of the remote cluster certificate. 

eg _CN=*.mycluster.prod.com,CN=*.mycluster.dr.com_


## Future Use

### (future release) Version Query String Parameter Name (`VersionParameter`) (optional)
Name of query string parameter which contains the version number of the application to locate 

eg `methodic-api-version` used as `https://mycoolservice.methodic.com/items?methodic-api-version=v1.2.3.4`

### (future release) Clear Cache Query String Parameter Name (`ClearCacheParameter`) (optional)
If the query string parameter parses as a True boolean then all cached entries for Service Fabric are ignored and gets the application and services information from the cluster once again. Slower operation and should only be used for diagnostics

eg `no-cache` used as `https://mycoolservice.methodic.com/items?no-cache=true`


# Maintenance

### Logging
Logs are written to windows event log if `EnableLogging` is set to _True_
