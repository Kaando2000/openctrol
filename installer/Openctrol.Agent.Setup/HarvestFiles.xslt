<?xml version="1.0" encoding="UTF-8"?>
<xsl:stylesheet version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform" xmlns:wix="http://schemas.microsoft.com/wix/2006/wi">
  <xsl:output method="xml" indent="yes" />
  
  <xsl:template match="@*|node()">
    <xsl:copy>
      <xsl:apply-templates select="@*|node()" />
    </xsl:copy>
  </xsl:template>
  
  <!-- Exclude certain files -->
  <xsl:key name="exclude" match="wix:Component[contains(wix:File/@Source, '.pdb')]" use="@Id" />
  <xsl:template match="wix:Component[key('exclude', @Id)]" />
  <xsl:template match="wix:ComponentRef[key('exclude', @Id)]" />
  
  <!-- Exclude Class elements without ForeignServer or Server attribute -->
  <xsl:template match="wix:Class[not(@ForeignServer) and not(@Server)]" />
</xsl:stylesheet>

