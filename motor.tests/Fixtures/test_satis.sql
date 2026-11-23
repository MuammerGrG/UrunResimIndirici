# Host: localhost  (Version 5.5.21)
# Date: 2025-02-20 14:14:38
# Generator: MySQL-Front 6.1  (Build 1.26)


#
# Structure for table "tbl_hizlikategori"
#

DROP TABLE IF EXISTS `tbl_hizlikategori`;
CREATE TABLE `tbl_hizlikategori` (
  `hizlikategoriid` int(11) NOT NULL AUTO_INCREMENT,
  `hizlikategoriad` varchar(100) DEFAULT NULL,
  `hizlikategorizaman` datetime DEFAULT NULL,
  `hizlikategoripersonel` varchar(50) DEFAULT NULL,
  `hizlikategorisil` enum('Y','N') DEFAULT 'N',
  `hizlikategorisilzaman` datetime DEFAULT NULL,
  `hizlikategorisilpersonel` varchar(50) DEFAULT NULL,
  PRIMARY KEY (`hizlikategoriid`)
) ENGINE=InnoDB AUTO_INCREMENT=4 DEFAULT CHARSET=utf8;

#
# Data for table "tbl_hizlikategori"
#

INSERT INTO `tbl_hizlikategori` VALUES (1,'ESKİ BUTON','2024-04-25 14:36:15','DENEME','N',NULL,NULL);

#
# Structure for table "tbl_hizlikategoriurun"
#

DROP TABLE IF EXISTS `tbl_hizlikategoriurun`;
CREATE TABLE `tbl_hizlikategoriurun` (
  `hizlikategoriurunid` int(11) NOT NULL AUTO_INCREMENT,
  `hizlikategoriid` int(11) DEFAULT NULL,
  `hizliurunid` int(11) DEFAULT NULL,
  `hizliurunad` varchar(100) DEFAULT NULL,
  `hizliurunresim` blob,
  `hizliurunzaman` datetime DEFAULT NULL,
  `hizliurunpersonel` varchar(50) DEFAULT NULL,
  `hizliurunsil` enum('Y','N') DEFAULT 'N',
  `hizliurunsilzaman` datetime DEFAULT NULL,
  `hizliurunsilpersonel` varchar(50) DEFAULT NULL,
  PRIMARY KEY (`hizlikategoriurunid`)
) ENGINE=InnoDB AUTO_INCREMENT=30 DEFAULT CHARSET=utf8;

#
# Data for table "tbl_hizlikategoriurun"
#

INSERT INTO `tbl_hizlikategoriurun` VALUES (1,1,5,'ESKİ ÜRÜN',X'2F396A2F34',NULL,NULL,'N',NULL,NULL);

#
# Structure for table "tbl_urun"
#

DROP TABLE IF EXISTS `tbl_urun`;
CREATE TABLE `tbl_urun` (
  `urunid` int(11) NOT NULL AUTO_INCREMENT,
  `urunkategoriid` int(11) DEFAULT NULL,
  `urunkategoriad` varchar(100) DEFAULT NULL,
  `urunbarkod` varchar(20) DEFAULT NULL,
  `urunad` varchar(100) DEFAULT NULL,
  `urunaciklama` varchar(255) DEFAULT NULL,
  `urunbirim` enum('ADET','KG') DEFAULT 'ADET',
  `urunfiyat` float(10,2) DEFAULT '0.00',
  `urunadet` float(10,3) DEFAULT '0.000',
  `urunindirim` varchar(10) DEFAULT '0',
  `urunkomisyon` varchar(10) DEFAULT '0',
  `urunkampanya` int(10) DEFAULT NULL,
  `urunzaman` datetime DEFAULT NULL,
  `urunpersonel` varchar(50) DEFAULT NULL,
  `urunsil` enum('Y','N') DEFAULT 'N',
  `urunsilzaman` datetime DEFAULT NULL,
  `urunsilpersonel` varchar(50) DEFAULT NULL,
  PRIMARY KEY (`urunid`)
) ENGINE=InnoDB AUTO_INCREMENT=67051 DEFAULT CHARSET=utf8;

#
# Data for table "tbl_urun"
#

INSERT INTO `tbl_urun` VALUES (5,1,'BARKODLU','8690000000001','ESKİ ÜRÜN',NULL,'ADET',0.00,0.000,'0','0',NULL,NULL,NULL,'N',NULL,NULL),(6,2,'BARKODSUZ','002063000006','ŞEKER; KÜP',NULL,'ADET',10.00,0.000,'0','0',NULL,NULL,NULL,'N',NULL,NULL);

#
# Structure for table "tbl_urunkategori"
#

DROP TABLE IF EXISTS `tbl_urunkategori`;
CREATE TABLE `tbl_urunkategori` (
  `kategoriid` int(11) NOT NULL AUTO_INCREMENT,
  `kategoriad` varchar(100) DEFAULT NULL,
  `kategoriaciklama` varchar(255) DEFAULT NULL,
  `kategoriindirim` varchar(10) DEFAULT '0',
  `kategorikomisyon` varchar(10) DEFAULT '0',
  `kategorizaman` datetime DEFAULT NULL,
  `kategoripersonel` varchar(50) DEFAULT NULL,
  `kategorisil` enum('Y','N') DEFAULT 'N',
  `kategorisilzaman` datetime DEFAULT NULL,
  `kategorisilpersonel` varchar(50) DEFAULT NULL,
  `depidkdv` varchar(10) DEFAULT NULL,
  PRIMARY KEY (`kategoriid`)
) ENGINE=InnoDB AUTO_INCREMENT=3 DEFAULT CHARSET=utf8;

#
# Data for table "tbl_urunkategori"
#

INSERT INTO `tbl_urunkategori` VALUES (1,'BARKODLU',NULL,'0','0',NULL,NULL,'N',NULL,NULL,NULL),(2,'BARKODSUZ',NULL,'0','0','2024-04-25 14:34:23','DENEME','N',NULL,NULL,NULL);
